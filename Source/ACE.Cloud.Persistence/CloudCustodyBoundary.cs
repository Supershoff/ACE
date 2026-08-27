using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The application-level half of the World Boundary Authority's deposit and withdrawal handoffs
/// (ARCH-002, ARCH-006). Callers must be ACE world-boundary code holding a connection privileged to
/// read and write ace_shard; the narrowly privileged companion web identity (ARCH-004) must never
/// be given this class.
///
/// Both <see cref="DepositAsync"/> and <see cref="WithdrawAsync"/> follow the same protocol:
///   1. An idempotency check runs first, outside any new transaction: a repeated call with the same
///      idempotency key replays the already-committed result instead of re-running the handoff
///      (ARCH-006, transaction rules 4 and 8).
///   2. One MariaDB transaction performs deterministic row locking, revalidates the precondition
///      under that lock, mutates custody/possession, and appends the Activity Ledger and Custody
///      Outbox rows -- all before a single commit (transaction rule 5). Holding the lock from
///      validation through commit is what makes that validation also a commit-time revalidation:
///      there is no unlocked window in which the validated fact could change out from under it.
///   3. The idempotency record is written last, inside the same transaction, so a caller can never
///      observe a committed idempotency record whose underlying state change did not also commit.
///
/// This is a complementary, commit-time revalidation layer, not the only enforcement: the
/// ace_shard/ace_cloud triggers added by the AddCloudCustodyRecords migration already reject a
/// conflicting deposit at the database level (MariaDB CHECK constraints cannot express that
/// cross-schema lookup, so triggers are the primary database constraint). Revalidating here too
/// means a missing/misconfigured trigger cannot silently admit a conflict, and callers observe a
/// typed <see cref="CloudBoundaryOutcome{T}"/> instead of a raw provider exception.
/// </summary>
public sealed class CloudCustodyBoundary
{
    private readonly CloudDbContext _context;

    public CloudCustodyBoundary(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Moves a native biota with no current world possession into Cloud custody (ARCH-002,
    /// ARCH-005). Repeating this call with the same <paramref name="idempotencyKey"/> after a
    /// caller timeout, retry, or crash returns the original committed result rather than creating a
    /// second Cloud Custody Record (transaction rule 4).
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudCustodyRecord>> DepositAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        DepositAsync(biotaId, shardId, ownerId, idempotencyKey, faultInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload: <paramref name="faultInjector"/> is invoked at every named
    /// <see cref="CloudBoundaryFaultPoint"/> so fault-injection tests can simulate a crash at each
    /// boundary. Internal and reachable only from ACE.Cloud.PersistenceIntegrationTests
    /// (AssemblyInfo.cs); production callers always use the public overload above.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudCustodyRecord>> DepositAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken = default)
    {
        RequireIdempotencyKey(idempotencyKey);

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryDepositOnceAsync(biotaId, shardId, ownerId, idempotencyKey, faultInjector, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a deposit previously started with
    /// <paramref name="idempotencyKey"/>, or null if no such deposit has committed yet. A caller
    /// that timed out waiting for <see cref="DepositAsync"/> must call this instead of inferring
    /// failure (transaction rule 8): the original attempt may still be committing, or may already
    /// have committed without the timed-out caller observing the return value.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudCustodyRecord>?> TryGetDepositOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        return existing is null ? null : await ReplayDepositAsync(existing, cancellationToken);
    }

    /// <summary>
    /// Returns a native biota from Cloud custody to world possession by granting the recipient
    /// container (ARCH-002, ARCH-005). This is a minimal handoff prototype: it proves the crash-safe
    /// transactional protocol using a direct Container grant, not ACE's full inventory-receive
    /// validation (slots, burden, stack merges, uniqueness), which a later withdrawal-feature issue
    /// adds in front of this boundary.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudWithdrawalResult>> WithdrawAsync(
        Guid custodyRecordId,
        int expectedVersion,
        uint recipientContainerId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        WithdrawAsync(custodyRecordId, expectedVersion, recipientContainerId, idempotencyKey, faultInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload; see the internal <see cref="DepositAsync"/> overload's doc comment.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudWithdrawalResult>> WithdrawAsync(
        Guid custodyRecordId,
        int expectedVersion,
        uint recipientContainerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken = default)
    {
        RequireIdempotencyKey(idempotencyKey);

        if (recipientContainerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipientContainerId), "A withdrawal requires a real recipient container GUID.");
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryWithdrawOnceAsync(custodyRecordId, expectedVersion, recipientContainerId, idempotencyKey, faultInjector, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a withdrawal previously started with
    /// <paramref name="idempotencyKey"/>, or null if no such withdrawal has committed yet. See
    /// <see cref="TryGetDepositOutcomeAsync"/> for why a timed-out caller must call this instead of
    /// inferring failure.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudWithdrawalResult>?> TryGetWithdrawalOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        return ReplayWithdrawal(existing);
    }

    private async Task<CloudBoundaryOutcome<CloudCustodyRecord>> TryDepositOnceAsync(
        uint biotaId,
        string shardId,
        Guid ownerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return await ReplayDepositAsync(existing, cancellationToken);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        if (await HasWorldPossessionAsync(biotaId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict(
                $"Biota {biotaId} currently has world possession (Container, Wielder, or Location) and cannot enter Cloud custody.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        // Deposit does not itself remove world possession: HasWorldPossessionAsync above already
        // requires it to be absent (ACE's Cloud Custodian path is responsible for that earlier
        // step). This fault point still exists so the protocol shape matches WithdrawAsync's.
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

        var correlationId = Guid.NewGuid();
        var record = new CloudCustodyRecord(biotaId, shardId, ownerId, correlationId);
        _context.CloudCustodyRecords.Add(record);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict(
                $"Biota {biotaId} already has a Cloud Custody Record.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

        await AppendLedgerAndOutboxAsync(correlationId, shardId, CloudBoundaryOperationType.Deposit, biotaId, ownerId, faultInjector, cancellationToken);

        _context.CloudIdempotencyRecords.Add(
            new CloudIdempotencyRecord(
                idempotencyKey, shardId, CloudBoundaryOperationType.Deposit, biotaId, ownerId,
                custodyRecordId: record.Id, targetContainerId: null, correlationId));
        await _context.SaveChangesAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudCustodyRecord>.Committed(record);
    }

    private async Task<CloudBoundaryOutcome<CloudWithdrawalResult>> TryWithdrawOnceAsync(
        Guid custodyRecordId,
        int expectedVersion,
        uint recipientContainerId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return ReplayWithdrawal(existing);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Deterministic single-row lock (transaction rules 2 and 7): SELECT ... FOR UPDATE on the
        // exact Cloud Custody Record this withdrawal targets.
        var record = await LockCustodyRecordAsync(custodyRecordId, cancellationToken);
        if (record is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudWithdrawalResult>.Conflict(
                $"Cloud Custody Record {custodyRecordId} does not exist or was already withdrawn.");
        }

        if (record.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudWithdrawalResult>.Conflict(
                $"Cloud Custody Record {custodyRecordId} is at version {record.Version}, not the expected version {expectedVersion}.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        var biotaId = record.BiotaId;
        var shardId = record.ShardId;
        var ownerId = record.OwnerId;
        var correlationId = Guid.NewGuid();

        // Custody must be released before world possession is granted: the AddCloudCustodyRecords
        // migration's trg_biota_iid_reject_cloud_custodied_insert trigger refuses a Container grant
        // for a biota that still has a CloudCustodyRecord row. Deleting first, on the same
        // connection and transaction, means the trigger's own EXISTS check observes this
        // transaction's own uncommitted delete and lets the grant through.
        _context.CloudCustodyRecords.Remove(record);
        await _context.SaveChangesAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

        await GrantContainerAsync(biotaId, recipientContainerId, cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

        await AppendLedgerAndOutboxAsync(correlationId, shardId, CloudBoundaryOperationType.Withdrawal, biotaId, ownerId, faultInjector, cancellationToken);

        _context.CloudIdempotencyRecords.Add(
            new CloudIdempotencyRecord(
                idempotencyKey, shardId, CloudBoundaryOperationType.Withdrawal, biotaId, ownerId,
                custodyRecordId, recipientContainerId, correlationId));
        await _context.SaveChangesAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudWithdrawalResult>.Committed(new CloudWithdrawalResult(biotaId, recipientContainerId, ownerId));
    }

    private async Task AppendLedgerAndOutboxAsync(
        Guid correlationId,
        string shardId,
        CloudBoundaryOperationType operationType,
        uint biotaId,
        Guid ownerId,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.CloudActivityLedgerEvents.Add(
            new CloudActivityLedgerEvent(correlationId, shardId, operationType, biotaId, ownerId, CloudBoundaryOutcomeKind.Committed));
        await _context.SaveChangesAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterLedgerAppend);

        _context.CloudCustodyOutboxEvents.Add(
            new CloudCustodyOutboxEvent(correlationId, shardId, operationType, biotaId, ownerId));
        await _context.SaveChangesAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterOutboxAppend);
    }

    private async Task<CloudBoundaryOutcome<CloudCustodyRecord>> ReplayDepositAsync(
        CloudIdempotencyRecord existing, CancellationToken cancellationToken)
    {
        if (existing.OperationType != CloudBoundaryOperationType.Deposit)
        {
            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not a Deposit.");
        }

        var record = await _context.CloudCustodyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == existing.CustodyRecordId, cancellationToken);

        if (record is null)
        {
            // ARCH-006 commits the idempotency record and the CloudCustodyRecord insert in the
            // same transaction, so a committed Deposit idempotency record whose custody row is
            // gone is not a normal conflict -- it means that invariant was broken out of band.
            throw new CloudCustodyConflictException(
                $"Idempotency key {existing.IdempotencyKey} committed a deposit whose Cloud Custody Record no longer exists.");
        }

        return CloudBoundaryOutcome<CloudCustodyRecord>.Committed(record);
    }

    private static CloudBoundaryOutcome<CloudWithdrawalResult> ReplayWithdrawal(CloudIdempotencyRecord existing)
    {
        if (existing.OperationType != CloudBoundaryOperationType.Withdrawal)
        {
            return CloudBoundaryOutcome<CloudWithdrawalResult>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not a Withdrawal.");
        }

        return CloudBoundaryOutcome<CloudWithdrawalResult>.Committed(
            new CloudWithdrawalResult(existing.BiotaId, existing.TargetContainerId!.Value, existing.OwnerId));
    }

    private async Task<CloudIdempotencyRecord?> FindIdempotencyRecordAsync(Guid idempotencyKey, CancellationToken cancellationToken) =>
        await _context.CloudIdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

    private async Task<CloudCustodyRecord?> LockCustodyRecordAsync(Guid custodyRecordId, CancellationToken cancellationToken) =>
        await _context.CloudCustodyRecords
            .FromSqlInterpolated($"SELECT * FROM CloudCustodyRecord WHERE Id = {custodyRecordId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> HasWorldPossessionAsync(uint biotaId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            SELECT
                EXISTS (
                    SELECT 1 FROM ace_shard.biota_properties_i_i_d
                    WHERE object_Id = @biotaId AND type IN (2, 3)
                    FOR UPDATE
                )
                OR EXISTS (
                    SELECT 1 FROM ace_shard.biota_properties_position
                    WHERE object_Id = @biotaId AND position_Type = 1
                    FOR UPDATE
                );
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@biotaId";
        parameter.Value = biotaId;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && result is not DBNull && Convert.ToInt64(result) != 0;
    }

    private async Task GrantContainerAsync(uint biotaId, uint recipientContainerId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = """
            INSERT INTO ace_shard.biota_properties_i_i_d (object_Id, type, value)
            VALUES (@biotaId, 2, @containerId);
            """;

        var biotaParameter = command.CreateParameter();
        biotaParameter.ParameterName = "@biotaId";
        biotaParameter.Value = biotaId;
        command.Parameters.Add(biotaParameter);

        var containerParameter = command.CreateParameter();
        containerParameter.ParameterName = "@containerId";
        containerParameter.Value = recipientContainerId;
        command.Parameters.Add(containerParameter);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsDuplicateKey(DbUpdateException ex) =>
        ex.InnerException is MySqlConnector.MySqlException { Number: 1062 };

    private static void RequireIdempotencyKey(Guid idempotencyKey)
    {
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A world-boundary handoff requires a non-empty idempotency key.", nameof(idempotencyKey));
        }
    }

    private static Task InvokeFaultInjectorAsync(Func<CloudBoundaryFaultPoint, Task>? faultInjector, CloudBoundaryFaultPoint point) =>
        faultInjector is null ? Task.CompletedTask : faultInjector(point);
}
