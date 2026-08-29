using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's off-world "immediate cloud transfer" edge (IMPLEMENTATION-BRIEF.md's
/// core custody state model) for one whole (non-stack) Cloud Item: reassigns a
/// <see cref="CloudCustodyRecord"/> to a new owner outside any typed reservation's fulfillment --
/// for example a future Allegiance Vault contribution/take (<see cref="CloudCustodyRecord.ChangeOwner"/>'s
/// own doc comment) once that workflow exists. Every precondition is delegated to
/// <see cref="CloudOwnershipTransferPolicy"/> rather than re-implemented here (ARCH-003, ARCH-004,
/// ARCH-006, INV-001, EVT-001, EVT-002, transaction rules 1-10): an actively reserved target, a stale
/// expected version, or a same-owner no-op are all refused before anything commits.
///
/// Deliberately not part of <see cref="CloudCustodyBoundary"/>: this never touches ace_shard (no
/// Container/Wielder/Location write, no native GUID), so it belongs to the narrowly privileged
/// companion web identity, matching <see cref="CloudStackLotTransactionAuthority"/> and
/// <see cref="CloudAllegianceVaultGateway"/>'s own authority-boundary doc comments and enforced for
/// this project by <c>ACE.Cloud.RepositoryPolicyTests.CloudWorldBoundaryAuthoritySurfaceTests</c>.
///
/// Unlike <see cref="CloudStackLotTransactionAuthority"/>'s lot-only Transfer/Split/Merge (which
/// intentionally defer idempotency-key tracking, see that class's own doc comment), this transfer
/// threads an idempotency key through the shared <see cref="CloudIdempotencyRecord"/> and appends one
/// Activity Ledger event plus one Custody Outbox event in the same transaction as the owner change,
/// exactly like every <see cref="CloudCustodyBoundary"/> handoff -- because this is the first Cloud
/// Transaction Authority command this project generalizes far enough to prove that full pattern
/// (ledger, outbox, idempotent replay, deterministic locking, optimistic version, reservation
/// exclusivity, and the Global/Marketplace freeze precondition) is reusable outside ACE's own
/// ace_shard-privileged gateway.
///
/// Global Cloud Maintenance and Marketplace State are full administrative aggregates out of scope
/// for this issue (see <see cref="CloudMutationGateState"/>'s own doc comment); this authority
/// therefore always evaluates <see cref="CloudOwnershipTransferPolicy.Transfer"/> against
/// <see cref="CloudMutationGateState.Open"/>, matching every other Cloud Transaction Authority call
/// site established so far (for example <see cref="CloudAccountLinkGateway.LinkAsync"/>).
/// </summary>
public sealed class CloudOwnershipTransferAuthority
{
    private readonly CloudDbContext _context;

    public CloudOwnershipTransferAuthority(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Reassigns the whole Cloud Item backed by <paramref name="biotaId"/> to
    /// <paramref name="newOwnerId"/>. Repeating this call with the same
    /// <paramref name="idempotencyKey"/> replays the original committed result instead of
    /// transferring twice (transaction rule 4).
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudCustodyRecord>> TransferAsync(
        uint biotaId,
        Guid newOwnerId,
        int expectedVersion,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        TransferAsync(biotaId, newOwnerId, expectedVersion, idempotencyKey, faultInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload: <paramref name="faultInjector"/> is invoked at every named
    /// <see cref="CloudBoundaryFaultPoint"/> so fault-injection tests can simulate a crash at each
    /// boundary, matching <see cref="CloudCustodyBoundary"/>'s established pattern. Internal and
    /// reachable only from ACE.Cloud.PersistenceIntegrationTests (AssemblyInfo.cs); production
    /// callers always use the public overload above.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudCustodyRecord>> TransferAsync(
        uint biotaId,
        Guid newOwnerId,
        int expectedVersion,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A Cloud ownership transfer requires a non-empty idempotency key.", nameof(idempotencyKey));
        }

        if (newOwnerId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud ownership transfer requires a target owner.", nameof(newOwnerId));
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryTransferOnceAsync(biotaId, newOwnerId, expectedVersion, idempotencyKey, faultInjector, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a transfer previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet (transaction rule 8): a
    /// timed-out caller must call this instead of inferring failure.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudCustodyRecord>?> TryGetTransferOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        return existing is null ? null : await ReplayAsync(existing, cancellationToken);
    }

    private async Task<CloudBoundaryOutcome<CloudCustodyRecord>> TryTransferOnceAsync(
        uint biotaId,
        Guid newOwnerId,
        int expectedVersion,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var existingByKey = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existingByKey is not null)
        {
            return await ReplayAsync(existingByKey, cancellationToken);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var record = await LockCustodyRecordByBiotaIdAsync(biotaId, cancellationToken);

        // Two concurrent callers racing the same idempotency key both pass the unlocked check above
        // before either commits; the loser blocks on the row lock above until the winner commits,
        // then would otherwise see the now-bumped version and misreport an unrelated-looking
        // version conflict instead of the replay transaction rule 8 requires. Re-checking here, right
        // after the row lock a winner's commit would have released, sees that winner's already-
        // committed idempotency record (InnoDB's locking read establishes this transaction's
        // consistent-read snapshot no earlier than this point).
        var existingByKeyAfterLock = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existingByKeyAfterLock is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReplayAsync(existingByKeyAfterLock, cancellationToken);
        }

        if (record is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict($"Biota {biotaId} has no Cloud Custody Record to transfer.");
        }

        if (record.IsStack)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict(
                $"Biota {biotaId} is a stack Cloud Custody Record; transfer its Cloud Stack Lot(s) with "
                    + $"{nameof(CloudStackLotTransactionAuthority)} instead.");
        }

        var target = CloudReservationTarget.ForItem(new CloudItemId(biotaId));
        var activeAllocation = await FindActiveReservationAllocationAsync(target, cancellationToken);

        var policyResult = CloudOwnershipTransferPolicy.Transfer(
            target,
            new CloudAccountId(record.OwnerId!.Value),
            new CloudAccountId(newOwnerId),
            new CloudAggregateVersion(record.Version),
            new CloudAggregateVersion(expectedVersion),
            activeAllocation,
            CloudMutationGateState.Open);

        if (!policyResult.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict(policyResult.Reason!);
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        var shardId = record.ShardId;
        record.ChangeOwner(newOwnerId);
        _context.CloudCustodyRecords.Update(record);
        await _context.SaveChangesAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

        var correlationId = Guid.NewGuid();
        _context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
            correlationId, shardId, CloudBoundaryOperationType.OwnershipTransfer, biotaId, newOwnerId, CloudBoundaryOutcomeKind.Committed));
        await _context.SaveChangesAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterLedgerAppend);

        var sequenceNumber = await ReserveNextOutboxSequenceNumberAsync(cancellationToken);
        _context.CloudCustodyOutboxEvents.Add(new CloudCustodyOutboxEvent(
            correlationId, shardId, CloudBoundaryOperationType.OwnershipTransfer, biotaId, newOwnerId, sequenceNumber));
        await _context.SaveChangesAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterOutboxAppend);

        _context.CloudIdempotencyRecords.Add(new CloudIdempotencyRecord(
            idempotencyKey, shardId, CloudBoundaryOperationType.OwnershipTransfer, biotaId, newOwnerId,
            record.Id, targetContainerId: null, correlationId));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsDuplicateKey(ex))
        {
            // A concurrent transfer for this exact idempotency key won the race between the
            // unlocked check above and this insert. Replay whichever attempt actually committed
            // instead of reporting an unrelated-looking Conflict (transaction rules 4 and 8).
            await transaction.RollbackAsync(cancellationToken);

            var winner = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return await ReplayAsync(winner, cancellationToken);
            }

            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict(
                "A concurrent Cloud ownership transfer for this idempotency key already committed.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudCustodyRecord>.Committed(record);
    }

    private async Task<CloudBoundaryOutcome<CloudCustodyRecord>> ReplayAsync(
        CloudIdempotencyRecord existing, CancellationToken cancellationToken)
    {
        if (existing.OperationType != CloudBoundaryOperationType.OwnershipTransfer)
        {
            return CloudBoundaryOutcome<CloudCustodyRecord>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not an OwnershipTransfer.");
        }

        var record = await _context.CloudCustodyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == existing.CustodyRecordId, cancellationToken);
        if (record is null)
        {
            // ARCH-006 commits the idempotency record and the CloudCustodyRecord update in the
            // same transaction, so a committed transfer whose custody row is gone is not a normal
            // conflict -- it means that invariant was broken out of band.
            throw new CloudCustodyConflictException(
                $"Idempotency key {existing.IdempotencyKey} committed an ownership transfer whose Cloud Custody Record no longer exists.");
        }

        return CloudBoundaryOutcome<CloudCustodyRecord>.Committed(record);
    }

    /// <summary>
    /// True (as an allocation, not just a bool) when <paramref name="target"/> is currently the
    /// target of an active exclusive reservation of any kind (WDR-001/INV-001) -- today the only
    /// persisted reservation kind is Withdrawal, but this reads generically off
    /// <see cref="CloudReservationTargetKind"/> so a later Listing/Offer/BidEscrow reservation table
    /// only needs to be unioned in here, not duplicated as a second policy check. Callers must
    /// already hold the target's row lock so this check and the mutation it guards happen atomically
    /// under the same transaction.
    /// </summary>
    private async Task<CloudReservationAllocation?> FindActiveReservationAllocationAsync(
        CloudReservationTarget target, CancellationToken cancellationToken)
    {
        var biotaId = target.ItemId!.Value;

        var activeReservationId = await (
            from t in _context.CloudWithdrawalReservationTargets.AsNoTracking()
            join r in _context.CloudWithdrawalReservations.AsNoTracking() on t.ReservationId equals r.Id
            where t.Kind == CloudWithdrawalReservationTargetKind.Item && t.ItemBiotaId == biotaId && r.Status == CloudReservationStatus.Active
            select (Guid?)r.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return activeReservationId is null
            ? null
            : new CloudReservationAllocation(
                new CloudReservationId(activeReservationId.Value), target, CloudReservationKind.Withdrawal, CloudReservationStatus.Active);
    }

    private async Task<CloudCustodyRecord?> LockCustodyRecordByBiotaIdAsync(uint biotaId, CancellationToken cancellationToken) =>
        await _context.CloudCustodyRecords
            .FromSqlInterpolated($"SELECT * FROM CloudCustodyRecord WHERE BiotaId = {biotaId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudIdempotencyRecord?> FindIdempotencyRecordAsync(Guid idempotencyKey, CancellationToken cancellationToken) =>
        await _context.CloudIdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

    /// <summary>
    /// Locks <see cref="CloudCustodyOutboxSequence"/>'s single row and returns the next durable order
    /// position, the same locking approach <see cref="CloudCustodyBoundary"/> and
    /// <see cref="CloudAllegianceVaultGateway"/> use for every other Custody Outbox append (ARCH-007).
    /// </summary>
    private async Task<long> ReserveNextOutboxSequenceNumberAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        long reserved;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT NextValue FROM CloudCustodyOutboxSequence WHERE Id = 1 FOR UPDATE;";
            reserved = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken));
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE CloudCustodyOutboxSequence SET NextValue = @nextValue WHERE Id = 1;";
            CloudRawSqlHelpers.AddParameter(update, "@nextValue", reserved + 1);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return reserved;
    }

    private static Task InvokeFaultInjectorAsync(Func<CloudBoundaryFaultPoint, Task>? faultInjector, CloudBoundaryFaultPoint point) =>
        faultInjector is null ? Task.CompletedTask : faultInjector(point);
}
