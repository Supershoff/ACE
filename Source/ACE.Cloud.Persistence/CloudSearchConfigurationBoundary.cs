using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The application-level half of Safe Regex Search's versioned admin-disablement persistence
/// (SRCH-001), matching <see cref="CloudMarketplaceConfigurationBoundary"/>'s established
/// lock-then-revalidate-then-commit shape for a singleton admin-config aggregate.
/// </summary>
public sealed class CloudSearchConfigurationBoundary
{
    private readonly CloudDbContext _context;

    public CloudSearchConfigurationBoundary(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Reads the current configuration, bootstrapping the out-of-the-box default row (regex enabled)
    /// on the first-ever read for <paramref name="shardId"/>. Concurrent first-ever reads race safely:
    /// a losing bootstrap attempt replays the winner's committed row instead of erroring.
    /// </summary>
    public async Task<CloudSearchConfiguration> GetCurrentAsync(string shardId, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        var existing = await ReadCurrentAsync(shardId, cancellationToken);
        if (existing is not null)
        {
            return existing.ToDomain();
        }

        var defaultRow = CloudSearchConfigurationRecord.CreateDefault(shardId);
        _context.Set<CloudSearchConfigurationRecord>().Add(defaultRow);

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
        catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsForeignKeyViolation(ex))
        {
            // shardId does not match this deployment's one bound Cloud Shard (ARCH-001,
            // CloudShardBinding's own singleton constraint) -- there is no shard for a row to
            // bootstrap against and never will be, so report the same out-of-the-box default an
            // administrator would see rather than surface a raw database constraint failure to a
            // caller that (like CloudInventoryCandidateReader for the same mismatched shardId) is
            // only ever going to see an empty, unauthorized-nothing result anyway.
            _context.ChangeTracker.Clear();
            return CloudSearchConfiguration.Default();
        }

        return defaultRow.ToDomain();
    }

    public async Task<CloudBoundaryOutcome<CloudSearchConfiguration>> SetRegexSearchEnabledAsync(
        string shardId, bool requested, uint actorAccessLevel, int expectedVersion, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var row = await LockConfigurationRowAsync(shardId, cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudSearchConfiguration>.Conflict(
                $"No Safe Regex Search configuration exists yet for shard {shardId}; call {nameof(GetCurrentAsync)} first to bootstrap it.");
        }

        if (row.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudSearchConfiguration>.Conflict(
                $"Safe Regex Search configuration is at version {row.Version}, not the expected version {expectedVersion}.");
        }

        var current = row.ToDomain();
        var result = CloudSearchConfigurationPolicy.SetRegexSearchEnabled(current, requested, actorAccessLevel);

        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudSearchConfiguration>.Conflict(result.Reason!);
        }

        row.ApplyScalars(result.Configuration!);
        _context.Set<CloudSearchConfigurationRecord>().Update(row);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudSearchConfiguration>.Committed(result.Configuration!);
    }

    private async Task<CloudSearchConfigurationRecord?> ReadCurrentAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.Set<CloudSearchConfigurationRecord>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShardId == shardId, cancellationToken);

    private async Task<CloudSearchConfigurationRecord?> LockConfigurationRowAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.Set<CloudSearchConfigurationRecord>()
            .FromSqlInterpolated($"SELECT * FROM CloudSearchConfiguration WHERE ShardId = {shardId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static void RequireShardId(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Safe Regex Search configuration operation requires a Cloud Shard ID.", nameof(shardId));
        }
    }
}
