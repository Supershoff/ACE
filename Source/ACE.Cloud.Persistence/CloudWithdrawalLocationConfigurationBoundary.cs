using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The application-level half of Withdrawal Landblock configuration's versioned persistence and
/// hot-apply protocol (WDR-006, ADM-003). Mirrors <see cref="CloudCustodianConfigurationBoundary"/>'s
/// lock-then-revalidate-then-commit shape exactly (transaction rules 2, 3, 5, 9): every mutating
/// method locks the singleton <see cref="CloudWithdrawalLocationConfigurationRecord"/> row for the
/// whole transaction, converts it (plus its named landblocks) into the pure
/// <see cref="CloudWithdrawalLocationConfiguration"/> domain aggregate, delegates the actual
/// validation/transition to <see cref="CloudWithdrawalLocationConfigurationPolicy"/>, and -- only on
/// success -- persists the result and commits.
/// </summary>
public sealed class CloudWithdrawalLocationConfigurationBoundary
{
    private readonly CloudDbContext _context;

    public CloudWithdrawalLocationConfigurationBoundary(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Reads the current configuration, bootstrapping the out-of-the-box default row on the
    /// first-ever read for <paramref name="shardId"/>. Concurrent first-ever reads race safely: a
    /// losing bootstrap attempt replays the winner's committed row instead of erroring.
    /// </summary>
    public async Task<CloudWithdrawalLocationConfiguration> GetCurrentAsync(string shardId, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        var (configRow, namedLandblockRows) = await ReadCurrentAsync(shardId, cancellationToken);
        if (configRow is not null)
        {
            return configRow.ToDomain(namedLandblockRows);
        }

        var defaultRow = CloudWithdrawalLocationConfigurationRecord.CreateDefault(shardId);
        _context.CloudWithdrawalLocationConfigurations.Add(defaultRow);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            _context.ChangeTracker.Clear();
            var (winnerRow, winnerNamedLandblocks) = await ReadCurrentAsync(shardId, cancellationToken);
            return winnerRow!.ToDomain(winnerNamedLandblocks);
        }

        return defaultRow.ToDomain([]);
    }

    public Task<CloudBoundaryOutcome<CloudWithdrawalLocationConfiguration>> SetWithdrawAnywhereEnabledAsync(
        string shardId, bool enabled, int expectedVersion, CancellationToken cancellationToken = default) =>
        ApplyAsync(
            shardId, expectedVersion,
            current => CloudWithdrawalLocationConfigurationPolicy.SetWithdrawAnywhereEnabled(current, enabled),
            cancellationToken);

    public Task<CloudBoundaryOutcome<CloudWithdrawalLocationConfiguration>> AddNamedLandblockAsync(
        string shardId, ushort landblock, string name, int expectedVersion, CancellationToken cancellationToken = default) =>
        ApplyAsync(
            shardId, expectedVersion,
            current => CloudWithdrawalLocationConfigurationPolicy.AddNamedLandblock(current, Guid.NewGuid(), landblock, name),
            cancellationToken);

    public Task<CloudBoundaryOutcome<CloudWithdrawalLocationConfiguration>> RemoveNamedLandblockAsync(
        string shardId, Guid landblockId, int expectedVersion, CancellationToken cancellationToken = default) =>
        ApplyAsync(
            shardId, expectedVersion,
            current => CloudWithdrawalLocationConfigurationPolicy.RemoveNamedLandblock(current, landblockId),
            cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudWithdrawalLocationConfiguration>> ApplyAsync(
        string shardId,
        int expectedVersion,
        Func<CloudWithdrawalLocationConfiguration, CloudWithdrawalLocationConfigurationChangeResult> transition,
        CancellationToken cancellationToken)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var configRow = await LockConfigurationRowAsync(shardId, cancellationToken);
        if (configRow is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudWithdrawalLocationConfiguration>.Conflict(
                $"No Withdrawal Location configuration exists yet for shard {shardId}; call {nameof(GetCurrentAsync)} first to bootstrap it.");
        }

        if (configRow.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudWithdrawalLocationConfiguration>.Conflict(
                $"Withdrawal Location configuration is at version {configRow.Version}, not the expected version {expectedVersion}.");
        }

        var namedLandblockRows = await _context.CloudWithdrawalNamedLandblocks.AsNoTracking()
            .Where(row => row.ShardId == shardId)
            .ToListAsync(cancellationToken);

        var current = configRow.ToDomain(namedLandblockRows);
        var result = transition(current);

        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudWithdrawalLocationConfiguration>.Conflict(result.Reason!);
        }

        var next = result.Configuration!;
        configRow.ApplyScalars(next);
        _context.CloudWithdrawalLocationConfigurations.Update(configRow);

        ReconcileNamedLandblocks(shardId, namedLandblockRows, next.NamedLandblocks);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudWithdrawalLocationConfiguration>.Committed(next);
    }

    private void ReconcileNamedLandblocks(
        string shardId,
        IReadOnlyList<CloudWithdrawalNamedLandblockRecord> existingRows,
        IReadOnlyList<CloudWithdrawalNamedLandblock> desired)
    {
        var desiredIds = desired.Select(l => l.Id).ToHashSet();
        var existingIds = existingRows.Select(r => r.Id).ToHashSet();

        var toRemove = existingRows.Where(r => !desiredIds.Contains(r.Id)).ToList();
        if (toRemove.Count > 0)
        {
            _context.CloudWithdrawalNamedLandblocks.RemoveRange(toRemove);
        }

        var toAdd = desired
            .Where(l => !existingIds.Contains(l.Id))
            .Select(l => new CloudWithdrawalNamedLandblockRecord(l.Id, shardId, l.Landblock, l.Name))
            .ToList();
        if (toAdd.Count > 0)
        {
            _context.CloudWithdrawalNamedLandblocks.AddRange(toAdd);
        }
    }

    private async Task<(CloudWithdrawalLocationConfigurationRecord? Config, List<CloudWithdrawalNamedLandblockRecord> NamedLandblocks)> ReadCurrentAsync(
        string shardId, CancellationToken cancellationToken)
    {
        var configRow = await _context.CloudWithdrawalLocationConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShardId == shardId, cancellationToken);

        var namedLandblockRows = await _context.CloudWithdrawalNamedLandblocks.AsNoTracking()
            .Where(row => row.ShardId == shardId)
            .ToListAsync(cancellationToken);

        return (configRow, namedLandblockRows);
    }

    private async Task<CloudWithdrawalLocationConfigurationRecord?> LockConfigurationRowAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.CloudWithdrawalLocationConfigurations
            .FromSqlInterpolated($"SELECT * FROM CloudWithdrawalLocationConfiguration WHERE ShardId = {shardId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static void RequireShardId(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Withdrawal Location configuration operation requires a Cloud Shard ID.", nameof(shardId));
        }
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is MySqlConnector.MySqlException { Number: 1062 };
}
