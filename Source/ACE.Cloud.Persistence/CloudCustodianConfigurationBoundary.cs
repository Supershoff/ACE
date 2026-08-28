using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The application-level half of Custodian configuration's versioned persistence and hot-apply
/// protocol (DEP-007, DEP-008, ADM-003). Every mutating method locks the singleton
/// <see cref="CloudCustodianConfigurationRecord"/> row for the whole transaction, converts it (plus
/// its custom positions) into the pure <see cref="CloudCustodianConfiguration"/> domain aggregate,
/// delegates the actual validation/transition to <see cref="CloudCustodianConfigurationPolicy"/>, and
/// -- only on success -- persists the result and commits. This is the same
/// lock-then-revalidate-then-commit shape <see cref="CloudCustodyBoundary"/> uses for custody
/// transitions (transaction rules 2, 3, 5, 9), so an admin configuration change is exactly as
/// crash-safe and race-free as a deposit or withdrawal: two concurrent admin edits against the same
/// expected version can never both win, and a change survives commit even if the ACE process
/// restarts immediately after (DEP-008: "persist while ACE is down").
/// </summary>
public sealed class CloudCustodianConfigurationBoundary
{
    private readonly CloudDbContext _context;

    public CloudCustodianConfigurationBoundary(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Reads the current configuration, bootstrapping the out-of-the-box default row (DEP-007:
    /// "Default Custodian locations are every mansion and Marketplace") on the first-ever read for
    /// <paramref name="shardId"/>. Concurrent first-ever reads race safely: a losing bootstrap
    /// attempt replays the winner's committed row instead of erroring.
    /// </summary>
    public async Task<CloudCustodianConfiguration> GetCurrentAsync(string shardId, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        var (configRow, customPositionRows) = await ReadCurrentAsync(shardId, cancellationToken);
        if (configRow is not null)
        {
            return configRow.ToDomain(customPositionRows);
        }

        var defaultRow = CloudCustodianConfigurationRecord.CreateDefault(shardId);
        _context.CloudCustodianConfigurations.Add(defaultRow);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            _context.ChangeTracker.Clear();
            var (winnerRow, winnerCustomPositions) = await ReadCurrentAsync(shardId, cancellationToken);
            return winnerRow!.ToDomain(winnerCustomPositions);
        }

        return defaultRow.ToDomain([]);
    }

    public Task<CloudBoundaryOutcome<CloudCustodianConfiguration>> SetMarketplaceEnabledAsync(
        string shardId, bool enabled, int expectedVersion, CancellationToken cancellationToken = default) =>
        ApplyAsync(shardId, expectedVersion, current => CloudCustodianConfigurationPolicy.SetMarketplaceEnabled(current, enabled), cancellationToken);

    public Task<CloudBoundaryOutcome<CloudCustodianConfiguration>> SetMansionsEnabledAsync(
        string shardId, bool enabled, int expectedVersion, CancellationToken cancellationToken = default) =>
        ApplyAsync(shardId, expectedVersion, current => CloudCustodianConfigurationPolicy.SetMansionsEnabled(current, enabled), cancellationToken);

    public Task<CloudBoundaryOutcome<CloudCustodianConfiguration>> AddCustomPositionAsync(
        string shardId, string rawPosition, int expectedVersion, CancellationToken cancellationToken = default) =>
        ApplyAsync(
            shardId, expectedVersion,
            current => CloudCustodianConfigurationPolicy.AddCustomPosition(current, Guid.NewGuid(), rawPosition),
            cancellationToken);

    public Task<CloudBoundaryOutcome<CloudCustodianConfiguration>> RemoveCustomPositionAsync(
        string shardId, Guid positionId, int expectedVersion, CancellationToken cancellationToken = default) =>
        ApplyAsync(
            shardId, expectedVersion,
            current => CloudCustodianConfigurationPolicy.RemoveCustomPosition(current, positionId),
            cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudCustodianConfiguration>> ApplyAsync(
        string shardId,
        int expectedVersion,
        Func<CloudCustodianConfiguration, CloudCustodianConfigurationChangeResult> transition,
        CancellationToken cancellationToken)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var configRow = await LockConfigurationRowAsync(shardId, cancellationToken);
        if (configRow is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudCustodianConfiguration>.Conflict(
                $"No Custodian configuration exists yet for shard {shardId}; call {nameof(GetCurrentAsync)} first to bootstrap it.");
        }

        if (configRow.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudCustodianConfiguration>.Conflict(
                $"Custodian configuration is at version {configRow.Version}, not the expected version {expectedVersion}.");
        }

        var customPositionRows = await _context.CloudCustodianCustomPositions.AsNoTracking()
            .Where(row => row.ShardId == shardId)
            .ToListAsync(cancellationToken);

        var current = configRow.ToDomain(customPositionRows);
        var result = transition(current);

        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudCustodianConfiguration>.Conflict(result.Reason!);
        }

        var next = result.Configuration!;
        configRow.ApplyScalars(next);
        _context.CloudCustodianConfigurations.Update(configRow);

        ReconcileCustomPositions(shardId, customPositionRows, next.CustomPositions);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudCustodianConfiguration>.Committed(next);
    }

    private void ReconcileCustomPositions(
        string shardId,
        IReadOnlyList<CloudCustodianCustomPositionRecord> existingRows,
        IReadOnlyList<CloudCustodianCustomPosition> desired)
    {
        var desiredIds = desired.Select(p => p.Id).ToHashSet();
        var existingIds = existingRows.Select(r => r.Id).ToHashSet();

        var toRemove = existingRows.Where(r => !desiredIds.Contains(r.Id)).ToList();
        if (toRemove.Count > 0)
        {
            _context.CloudCustodianCustomPositions.RemoveRange(toRemove);
        }

        var toAdd = desired
            .Where(p => !existingIds.Contains(p.Id))
            .Select(p => new CloudCustodianCustomPositionRecord(p.Id, shardId, p.Position.Raw))
            .ToList();
        if (toAdd.Count > 0)
        {
            _context.CloudCustodianCustomPositions.AddRange(toAdd);
        }
    }

    private async Task<(CloudCustodianConfigurationRecord? Config, List<CloudCustodianCustomPositionRecord> CustomPositions)> ReadCurrentAsync(
        string shardId, CancellationToken cancellationToken)
    {
        var configRow = await _context.CloudCustodianConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShardId == shardId, cancellationToken);

        var customPositionRows = await _context.CloudCustodianCustomPositions.AsNoTracking()
            .Where(row => row.ShardId == shardId)
            .ToListAsync(cancellationToken);

        return (configRow, customPositionRows);
    }

    private async Task<CloudCustodianConfigurationRecord?> LockConfigurationRowAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.CloudCustodianConfigurations
            .FromSqlInterpolated($"SELECT * FROM CloudCustodianConfiguration WHERE ShardId = {shardId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static void RequireShardId(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Custodian configuration operation requires a Cloud Shard ID.", nameof(shardId));
        }
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is MySqlConnector.MySqlException { Number: 1062 };
}
