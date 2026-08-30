using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The application-level half of Marketplace State's versioned persistence (MKT-203, MKT-204),
/// matching <see cref="CloudCustodianConfigurationBoundary"/>'s established lock-then-revalidate-then-
/// commit shape for a singleton admin-config aggregate.
/// </summary>
public sealed class CloudMarketplaceConfigurationBoundary
{
    private readonly CloudDbContext _context;

    public CloudMarketplaceConfigurationBoundary(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Reads the current configuration, bootstrapping the out-of-the-box default row (Enabled) on the
    /// first-ever read for <paramref name="shardId"/>. Concurrent first-ever reads race safely: a
    /// losing bootstrap attempt replays the winner's committed row instead of erroring.
    /// </summary>
    public async Task<CloudMarketplaceConfiguration> GetCurrentAsync(string shardId, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        var existing = await ReadCurrentAsync(shardId, cancellationToken);
        if (existing is not null)
        {
            return existing.ToDomain();
        }

        var defaultRow = CloudMarketplaceConfigurationRecord.CreateDefault(shardId);
        _context.Set<CloudMarketplaceConfigurationRecord>().Add(defaultRow);

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

    public async Task<CloudBoundaryOutcome<CloudMarketplaceConfiguration>> SetStateAsync(
        string shardId, CloudMarketplaceState requested, uint actorAccessLevel, int expectedVersion, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var row = await LockConfigurationRowAsync(shardId, cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudMarketplaceConfiguration>.Conflict(
                $"No Marketplace State configuration exists yet for shard {shardId}; call {nameof(GetCurrentAsync)} first to bootstrap it.");
        }

        if (row.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudMarketplaceConfiguration>.Conflict(
                $"Marketplace State is at version {row.Version}, not the expected version {expectedVersion}.");
        }

        var current = row.ToDomain();
        var result = CloudMarketplaceStatePolicy.SetState(current, requested, actorAccessLevel);

        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudMarketplaceConfiguration>.Conflict(result.Reason!);
        }

        row.ApplyScalars(result.Configuration!);
        _context.Set<CloudMarketplaceConfigurationRecord>().Update(row);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudMarketplaceConfiguration>.Committed(result.Configuration!);
    }

    private async Task<CloudMarketplaceConfigurationRecord?> ReadCurrentAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.Set<CloudMarketplaceConfigurationRecord>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShardId == shardId, cancellationToken);

    private async Task<CloudMarketplaceConfigurationRecord?> LockConfigurationRowAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.Set<CloudMarketplaceConfigurationRecord>()
            .FromSqlInterpolated($"SELECT * FROM CloudMarketplaceConfiguration WHERE ShardId = {shardId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static void RequireShardId(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Marketplace State operation requires a Cloud Shard ID.", nameof(shardId));
        }
    }
}
