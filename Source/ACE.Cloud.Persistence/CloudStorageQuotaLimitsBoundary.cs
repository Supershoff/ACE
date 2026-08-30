using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The application-level half of Storage Quota limits' versioned persistence (INV-004), matching
/// <see cref="CloudCustodianConfigurationBoundary"/>'s established lock-then-revalidate-then-commit
/// shape for a singleton admin-config aggregate.
/// </summary>
public sealed class CloudStorageQuotaLimitsBoundary
{
    private readonly CloudDbContext _context;

    public CloudStorageQuotaLimitsBoundary(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Reads the current limits, bootstrapping the out-of-the-box default row (both scopes unlimited)
    /// on the first-ever read for <paramref name="shardId"/>. Concurrent first-ever reads race safely:
    /// a losing bootstrap attempt replays the winner's committed row instead of erroring.
    /// </summary>
    public async Task<CloudStorageQuotaLimits> GetCurrentAsync(string shardId, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        var existing = await ReadCurrentAsync(shardId, cancellationToken);
        if (existing is not null)
        {
            return existing.ToDomain();
        }

        var defaultRow = CloudStorageQuotaLimitsRecord.CreateDefault(shardId);
        _context.Set<CloudStorageQuotaLimitsRecord>().Add(defaultRow);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsDuplicateKey(ex))
        {
            _context.ChangeTracker.Clear();
            var winner = await ReadCurrentAsync(shardId, cancellationToken);
            return winner!.ToDomain();
        }

        return defaultRow.ToDomain();
    }

    public Task<CloudBoundaryOutcome<CloudStorageQuotaLimits>> SetPersonalLimitAsync(
        string shardId, int? limit, uint actorAccessLevel, int expectedVersion, CancellationToken cancellationToken = default) =>
        ApplyAsync(shardId, expectedVersion, current => CloudStorageQuotaPolicy.SetPersonalLimit(current, limit, actorAccessLevel), cancellationToken);

    public Task<CloudBoundaryOutcome<CloudStorageQuotaLimits>> SetVaultLimitAsync(
        string shardId, int? limit, uint actorAccessLevel, int expectedVersion, CancellationToken cancellationToken = default) =>
        ApplyAsync(shardId, expectedVersion, current => CloudStorageQuotaPolicy.SetVaultLimit(current, limit, actorAccessLevel), cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudStorageQuotaLimits>> ApplyAsync(
        string shardId,
        int expectedVersion,
        Func<CloudStorageQuotaLimits, CloudStorageQuotaLimitsChangeResult> transition,
        CancellationToken cancellationToken)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var row = await LockLimitsRowAsync(shardId, cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStorageQuotaLimits>.Conflict(
                $"No Storage Quota limits exist yet for shard {shardId}; call {nameof(GetCurrentAsync)} first to bootstrap them.");
        }

        if (row.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStorageQuotaLimits>.Conflict(
                $"Storage Quota limits are at version {row.Version}, not the expected version {expectedVersion}.");
        }

        var result = transition(row.ToDomain());

        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStorageQuotaLimits>.Conflict(result.Reason!);
        }

        row.ApplyScalars(result.Limits!);
        _context.Set<CloudStorageQuotaLimitsRecord>().Update(row);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudStorageQuotaLimits>.Committed(result.Limits!);
    }

    private async Task<CloudStorageQuotaLimitsRecord?> ReadCurrentAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.Set<CloudStorageQuotaLimitsRecord>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShardId == shardId, cancellationToken);

    private async Task<CloudStorageQuotaLimitsRecord?> LockLimitsRowAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.Set<CloudStorageQuotaLimitsRecord>()
            .FromSqlInterpolated($"SELECT * FROM CloudStorageQuotaLimits WHERE ShardId = {shardId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static void RequireShardId(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Storage Quota limits operation requires a Cloud Shard ID.", nameof(shardId));
        }
    }
}
