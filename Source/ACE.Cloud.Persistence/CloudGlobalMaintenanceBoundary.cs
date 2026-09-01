using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The application-level half of Global Cloud Maintenance's versioned persistence (ADM-004): locked,
/// commit-time-revalidated entry/exit that appends one Activity Ledger event, notifies the configured
/// admin webhook (best-effort, after commit -- never inside the transaction, so a slow or failing
/// webhook can never roll back or delay the maintenance transition itself), and -- on exit -- shifts
/// every open Withdrawal Reservation's expiry by the exact frozen duration (ADM-004: "resume by
/// shifting deadlines exactly... never cancel or unlock automatically"). Matches
/// <see cref="CloudCustodianConfigurationBoundary"/>'s established lock-then-revalidate-then-commit
/// shape for a singleton admin-config aggregate.
/// </summary>
public sealed class CloudGlobalMaintenanceBoundary
{
    private readonly CloudDbContext _context;
    private readonly IAdminMaintenanceNotifier _notifier;

    public CloudGlobalMaintenanceBoundary(CloudDbContext context)
        : this(context, NoOpAdminMaintenanceNotifier.Instance)
    {
    }

    public CloudGlobalMaintenanceBoundary(CloudDbContext context, IAdminMaintenanceNotifier notifier)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    }

    /// <summary>
    /// Reads the current state, bootstrapping the out-of-the-box default row (open, not frozen) on the
    /// first-ever read for <paramref name="shardId"/>. Concurrent first-ever reads race safely: a
    /// losing bootstrap attempt replays the winner's committed row instead of erroring.
    /// </summary>
    public async Task<CloudGlobalMaintenanceState> GetCurrentAsync(string shardId, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        var existing = await ReadCurrentAsync(shardId, cancellationToken);
        if (existing is not null)
        {
            return existing.ToDomain();
        }

        var defaultRow = CloudGlobalMaintenanceRecord.CreateDefault(shardId);
        _context.Set<CloudGlobalMaintenanceRecord>().Add(defaultRow);

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

    public async Task<CloudBoundaryOutcome<CloudGlobalMaintenanceState>> EnterAsync(
        string shardId,
        string reason,
        bool confirmed,
        uint actorAccessLevel,
        uint actorAccountId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var row = await LockMaintenanceRowAsync(shardId, cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudGlobalMaintenanceState>.Conflict(
                $"No Global Cloud Maintenance state exists yet for shard {shardId}; call {nameof(GetCurrentAsync)} first to bootstrap it.");
        }

        if (row.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudGlobalMaintenanceState>.Conflict(
                $"Global Cloud Maintenance is at version {row.Version}, not the expected version {expectedVersion}.");
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        var result = CloudGlobalMaintenancePolicy.Enter(row.ToDomain(), reason, confirmed, actorAccessLevel, actorAccountId, nowUtc);

        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudGlobalMaintenanceState>.Conflict(result.Reason!);
        }

        row.ApplyScalars(result.State!);
        _context.Set<CloudGlobalMaintenanceRecord>().Update(row);

        var correlationId = Guid.NewGuid();
        _context.Set<CloudGlobalMaintenanceLedgerEvent>().Add(new CloudGlobalMaintenanceLedgerEvent(
            correlationId, shardId, CloudGlobalMaintenanceLedgerEventType.Entered, reason, actorAccountId, frozenDurationSeconds: null));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await NotifyBestEffortAsync(shardId, CloudAdminMaintenanceNotificationKind.Entered, reason, nowUtc, cancellationToken);

        return CloudBoundaryOutcome<CloudGlobalMaintenanceState>.Committed(result.State!);
    }

    public async Task<CloudBoundaryOutcome<CloudGlobalMaintenanceState>> ExitAsync(
        string shardId,
        bool confirmed,
        uint actorAccessLevel,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var row = await LockMaintenanceRowAsync(shardId, cancellationToken);
        if (row is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudGlobalMaintenanceState>.Conflict(
                $"No Global Cloud Maintenance state exists yet for shard {shardId}; call {nameof(GetCurrentAsync)} first to bootstrap it.");
        }

        if (row.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudGlobalMaintenanceState>.Conflict(
                $"Global Cloud Maintenance is at version {row.Version}, not the expected version {expectedVersion}.");
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        var current = row.ToDomain();
        var reasonBeforeExit = current.Reason;
        var result = CloudGlobalMaintenancePolicy.Exit(current, confirmed, actorAccessLevel, nowUtc);

        if (!result.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudGlobalMaintenanceState>.Conflict(result.Reason!);
        }

        row.ApplyScalars(result.State!);
        _context.Set<CloudGlobalMaintenanceRecord>().Update(row);

        // ADM-004: "resume by shifting deadlines exactly" -- never cancel or unlock, only shift the
        // still-open expiry forward by exactly the duration mutations were frozen for. Withdrawal
        // Reservation and Transfer Offer (issue #35) are the persisted reservation-shaped kinds with
        // their own expiry clock today; a future Listing/BidEscrow reservation table joins this same
        // bulk shift once it exists.
        var frozenDurationSeconds = (long)result.FrozenDuration!.Value.TotalSeconds;
        if (frozenDurationSeconds > 0)
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE CloudWithdrawalReservation
                SET ExpiresAtUtc = DATE_ADD(ExpiresAtUtc, INTERVAL {frozenDurationSeconds} SECOND), Version = Version + 1
                WHERE ShardId = {shardId} AND Status = 'Active'
                """,
                cancellationToken);

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE CloudTransferOffer
                SET ExpiresAtUtc = DATE_ADD(ExpiresAtUtc, INTERVAL {frozenDurationSeconds} SECOND), Version = Version + 1
                WHERE ShardId = {shardId} AND Status = 'Pending'
                """,
                cancellationToken);
        }

        var correlationId = Guid.NewGuid();
        _context.Set<CloudGlobalMaintenanceLedgerEvent>().Add(new CloudGlobalMaintenanceLedgerEvent(
            correlationId, shardId, CloudGlobalMaintenanceLedgerEventType.Exited, reasonBeforeExit, actorAccountId: null, frozenDurationSeconds));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await NotifyBestEffortAsync(shardId, CloudAdminMaintenanceNotificationKind.Exited, reasonBeforeExit, nowUtc, cancellationToken);

        return CloudBoundaryOutcome<CloudGlobalMaintenanceState>.Committed(result.State!);
    }

    /// <summary>
    /// Never lets a webhook failure surface as this boundary's own failure (ADM-004's webhook is a
    /// best-effort notification, not a transaction precondition): the maintenance transition has
    /// already committed by the time this runs.
    /// </summary>
    private async Task NotifyBestEffortAsync(
        string shardId, CloudAdminMaintenanceNotificationKind kind, string? reason, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        try
        {
            await _notifier.NotifyAsync(new CloudAdminMaintenanceNotification(shardId, kind, reason, occurredAtUtc), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Best-effort: the committed maintenance transition must never be reported as failed
            // because an external webhook endpoint was unreachable or slow.
        }
    }

    private async Task<CloudGlobalMaintenanceRecord?> ReadCurrentAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.Set<CloudGlobalMaintenanceRecord>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShardId == shardId, cancellationToken);

    private async Task<CloudGlobalMaintenanceRecord?> LockMaintenanceRowAsync(string shardId, CancellationToken cancellationToken) =>
        await _context.Set<CloudGlobalMaintenanceRecord>()
            .FromSqlInterpolated($"SELECT * FROM CloudGlobalMaintenance WHERE ShardId = {shardId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<DateTime> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT UTC_TIMESTAMP(6);";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return DateTime.SpecifyKind(Convert.ToDateTime(result), DateTimeKind.Utc);
    }

    private static void RequireShardId(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Global Cloud Maintenance operation requires a Cloud Shard ID.", nameof(shardId));
        }
    }
}
