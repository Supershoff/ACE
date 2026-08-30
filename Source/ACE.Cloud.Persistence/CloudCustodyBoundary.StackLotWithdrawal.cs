using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Issue #122's unified Withdrawal Reservation lifecycle (WDR-001, WDR-002, WDR-003, WDR-004,
/// WDR-005, WDR-008, INV-002, INV-003, ARCH-002, ARCH-006, EVT-001, EVT-002): one Withdrawal Token
/// reserves, and later redeems, an arbitrary mixed selection of whole Cloud Items and Cloud Stack Lot
/// quantities as a single atomic aggregate. This file predates that generalization (it originally
/// held only the Cloud Stack Lot half of a two-table design split from whole-item reservations in
/// <c>CloudCustodyBoundary.cs</c>); every reservation/redemption method for every target kind now
/// lives here together, because the previous split -- two independent tables, each with its own
/// <c>TokenHash</c> uniqueness constraint -- was exactly the bug this issue corrects: the same token
/// secret could open one row in each table at once, addressing two independently consumable
/// reservations from a single high-entropy secret.
///
/// Every public entry point below operates on the single <see cref="CloudWithdrawalReservation"/>
/// aggregate plus its <see cref="CloudWithdrawalReservationTarget"/> child rows. Multi-target
/// exclusivity and lock ordering reuse <see cref="CloudReservationPolicy"/> and
/// <see cref="CloudReservationTargetOrdering"/> (ACE.Cloud.Domain) unchanged -- the exact policy the
/// companion backend's own multi-target reservation workflows (listings, offers, escrow) already
/// share -- rather than duplicating exclusivity/ordering rules here.
/// </summary>
public sealed partial class CloudCustodyBoundary
{
    private static readonly IReadOnlyDictionary<Guid, uint> EmptyMaterializedBiotaIdsByTargetId = new Dictionary<Guid, uint>();

    /// <summary>
    /// Opens ACE's local authority record for a new Withdrawal Token's exclusive reservation over
    /// every requested target -- any mix of whole Cloud Items and Cloud Stack Lots -- or none of them
    /// (WDR-001, WDR-002, WDR-003). Every target is locked in the same deterministic order
    /// (<see cref="CloudReservationTargetOrdering.Order"/>) regardless of the order the caller listed
    /// them in, so two concurrent multi-target reservations that overlap can never deadlock against
    /// each other (transaction rule 2). Repeating this call with the same
    /// <paramref name="idempotencyKey"/> replays the original committed reservation (transaction
    /// rule 4). A Cloud Stack Lot target always reserves that lot's entire current quantity; a caller
    /// who wants a smaller amount must first split a new lot for exactly that quantity through
    /// <see cref="CloudStackLotTransactionAuthority.SplitLotAsync"/> and reserve that new lot.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> ReserveForWithdrawalAsync(
        IReadOnlyList<CloudWithdrawalReservationRequestTarget> targets,
        string shardId,
        Guid ownerId,
        string tokenHash,
        TimeSpan timeToLive,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ReserveForWithdrawalAsync(targets, shardId, ownerId, tokenHash, timeToLive, idempotencyKey, faultInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload: <paramref name="faultInjector"/> is invoked at every named
    /// <see cref="CloudBoundaryFaultPoint"/> so fault-injection tests can simulate a crash at each
    /// boundary of a multi-target reservation open. Internal and reachable only from
    /// ACE.Cloud.PersistenceIntegrationTests (AssemblyInfo.cs); production callers always use the
    /// public overload above.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> ReserveForWithdrawalAsync(
        IReadOnlyList<CloudWithdrawalReservationRequestTarget> targets,
        string shardId,
        Guid ownerId,
        string tokenHash,
        TimeSpan timeToLive,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey(idempotencyKey);

        ArgumentNullException.ThrowIfNull(targets);
        if (targets.Count == 0)
        {
            throw new ArgumentException("A Withdrawal Reservation requires at least one target.", nameof(targets));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("A Withdrawal Reservation requires a Withdrawal Token hash.", nameof(tokenHash));
        }

        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "A Withdrawal Reservation's time-to-live must be positive.");
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryReserveForWithdrawalOnceAsync(targets, shardId, ownerId, tokenHash, timeToLive, idempotencyKey, faultInjector, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a reservation open previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet (transaction rule 8).
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudWithdrawalReservation>?> TryGetWithdrawalReservationOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CloudWithdrawalReservations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OpenIdempotencyKey == idempotencyKey, cancellationToken);
        return existing is null ? null : CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(existing);
    }

    /// <summary>
    /// Cancels an active Withdrawal Reservation and every one of its targets before redemption
    /// (WDR-003). Idempotent by construction rather than by a stored idempotency key: cancelling an
    /// already-cancelled reservation is a no-op success, while cancelling one already released for a
    /// different reason (for example already redeemed) is a Conflict.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> CancelWithdrawalReservationAsync(
        Guid reservationId, int expectedVersion, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(
            () => TryCancelWithdrawalReservationOnceAsync(reservationId, expectedVersion, cancellationToken),
            cancellationToken: cancellationToken);

    /// <summary>
    /// Reads whether a Withdrawal Token's local reservation is currently active without consuming it
    /// (WDR-008: ACE validates an already-issued token's local reservation and redemption rules even
    /// while the companion web service is unreachable). Returns null when no active reservation
    /// matches <paramref name="tokenHash"/> -- either none was ever opened, or it was already
    /// released. Callers use <see cref="GetReservationTargetsAsync"/> or
    /// <see cref="PreviewWithdrawalReservationAsync"/> to inspect the exact target set.
    /// </summary>
    public async Task<CloudWithdrawalReservation?> TryGetActiveWithdrawalReservationAsync(
        string tokenHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Looking up a Withdrawal Reservation requires its Withdrawal Token hash.", nameof(tokenHash));
        }

        return await _context.CloudWithdrawalReservations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.TokenHash == tokenHash && r.Status == CloudReservationStatus.Active, cancellationToken);
    }

    /// <summary>Every target row locked by <paramref name="reservationId"/>'s reservation, in no particular order.</summary>
    public async Task<IReadOnlyList<CloudWithdrawalReservationTarget>> GetReservationTargetsAsync(
        Guid reservationId, CancellationToken cancellationToken = default) =>
        await _context.CloudWithdrawalReservationTargets.AsNoTracking()
            .Where(t => t.ReservationId == reservationId)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Informational, unlocked preview of every target a Withdrawal Token's reservation locks, used
    /// solely by an ACE-side caller to decide -- before ever calling
    /// <see cref="RedeemWithdrawalReservationAsync(string, uint, IReadOnlyDictionary{Guid, uint}, Guid, CancellationToken)"/> --
    /// which Cloud Stack Lot targets need a freshly ACE-allocated materialized child GUID (ARCH-010)
    /// and what the prospective delivered items look like for a combined native-receive capacity
    /// check across the whole selection (WDR-005). Not itself a commit-time revalidation: redemption
    /// re-derives every one of these facts fresh under its own row locks and refuses the request if a
    /// stale preview turns out wrong. Returns null when no reservation matches
    /// <paramref name="tokenHash"/> at all, or a same-length null-free list otherwise.
    /// </summary>
    public async Task<IReadOnlyList<CloudWithdrawalReservationTargetPreview>?> PreviewWithdrawalReservationAsync(
        string tokenHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Previewing a Withdrawal Reservation requires its Withdrawal Token hash.", nameof(tokenHash));
        }

        var reservation = await _context.CloudWithdrawalReservations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.TokenHash == tokenHash, cancellationToken);
        if (reservation is null)
        {
            return null;
        }

        var targets = await GetReservationTargetsAsync(reservation.Id, cancellationToken);
        var previews = new List<CloudWithdrawalReservationTargetPreview>(targets.Count);

        foreach (var target in targets)
        {
            if (target.Kind == CloudWithdrawalReservationTargetKind.Item)
            {
                previews.Add(new CloudWithdrawalReservationTargetPreview(
                    target.Id, target.Kind, target.ItemBiotaId!.Value, Quantity: null, RequiresMaterialization: false));
                continue;
            }

            var lot = await _context.CloudStackLots.AsNoTracking()
                .SingleOrDefaultAsync(l => l.Id == target.StackLotId!.Value, cancellationToken);
            if (lot is null)
            {
                return null;
            }

            var record = await _context.CloudCustodyRecords.AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == lot.CustodyRecordId, cancellationToken);
            if (record is null)
            {
                return null;
            }

            var siblingCount = await _context.CloudStackLots.AsNoTracking()
                .CountAsync(l => l.CustodyRecordId == lot.CustodyRecordId && l.Id != lot.Id, cancellationToken);

            previews.Add(new CloudWithdrawalReservationTargetPreview(
                target.Id, target.Kind, record.BiotaId, lot.Quantity, RequiresMaterialization: siblingCount != 0));
        }

        return previews;
    }

    /// <summary>
    /// Redeems a Withdrawal Token whose reservation targets only whole Cloud Items (no Cloud Stack
    /// Lot requiring materialization). Equivalent to calling the full overload with an empty
    /// materialized-GUID map.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudMultiWithdrawalResult>> RedeemWithdrawalReservationAsync(
        string tokenHash, uint recipientContainerId, Guid idempotencyKey, CancellationToken cancellationToken = default) =>
        RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, EmptyMaterializedBiotaIdsByTargetId, idempotencyKey, cancellationToken);

    /// <summary>
    /// Redeems a Withdrawal Token: atomically performs the same custody-to-world transition as
    /// <see cref="WithdrawAsync"/> or <see cref="WithdrawLotAsync"/> for every one of the
    /// reservation's targets, and releases the reservation as fulfilled, in one transaction, so the
    /// reservation can never observably outlive (or be released without) its custody transitions
    /// (WDR-001, WDR-003) and a multi-target redemption always delivers every target or none of them.
    /// Refuses an expired or already-released reservation instead of redeeming it. Repeating this
    /// call with the same <paramref name="idempotencyKey"/> replays the original committed result
    /// (transaction rule 4). <paramref name="materializedBiotaIdsByTargetId"/> must supply an
    /// ACE-allocated (ARCH-010) child GUID, keyed by <see cref="CloudWithdrawalReservationTarget.Id"/>,
    /// for every Cloud Stack Lot target that is not the sole lot backing its stack; a required entry
    /// missing from the map refuses the whole redemption with a Conflict rather than guessing.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudMultiWithdrawalResult>> RedeemWithdrawalReservationAsync(
        string tokenHash,
        uint recipientContainerId,
        IReadOnlyDictionary<Guid, uint> materializedBiotaIdsByTargetId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, materializedBiotaIdsByTargetId, idempotencyKey, faultInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload; see <see cref="ReserveForWithdrawalAsync(IReadOnlyList{CloudWithdrawalReservationRequestTarget}, string, Guid, string, TimeSpan, Guid, Func{CloudBoundaryFaultPoint, Task}, CancellationToken)"/>'s
    /// doc comment.
    /// </summary>
    internal Task<CloudBoundaryOutcome<CloudMultiWithdrawalResult>> RedeemWithdrawalReservationAsync(
        string tokenHash,
        uint recipientContainerId,
        IReadOnlyDictionary<Guid, uint> materializedBiotaIdsByTargetId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        RequireIdempotencyKey(idempotencyKey);

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Redeeming a Withdrawal Reservation requires its Withdrawal Token hash.", nameof(tokenHash));
        }

        if (recipientContainerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipientContainerId), "A withdrawal requires a real recipient container GUID.");
        }

        ArgumentNullException.ThrowIfNull(materializedBiotaIdsByTargetId);

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryRedeemWithdrawalReservationOnceAsync(
                tokenHash, recipientContainerId, materializedBiotaIdsByTargetId, idempotencyKey, faultInjector, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a reservation redemption previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet (transaction rule 8).
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudMultiWithdrawalResult>?> TryGetWithdrawalRedemptionOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        return existing is null ? null : await ReplayWithdrawalReservationRedemptionAsync(existing, cancellationToken);
    }

    private async Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> TryReserveForWithdrawalOnceAsync(
        IReadOnlyList<CloudWithdrawalReservationRequestTarget> requestedTargets,
        string shardId,
        Guid ownerId,
        string tokenHash,
        TimeSpan timeToLive,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var incompatible = await CheckProtocolCompatibilityAsync<CloudWithdrawalReservation>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existingByKey = await _context.CloudWithdrawalReservations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OpenIdempotencyKey == idempotencyKey, cancellationToken);
        if (existingByKey is not null)
        {
            return CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(existingByKey);
        }

        // Deterministic multi-target lock order (transaction rule 2): every requested target, whole
        // item or stack lot, is locked in the exact same relative order two concurrent overlapping
        // reservation attempts would compute independently, so neither can deadlock the other.
        var orderedPolicyTargets = CloudReservationTargetOrdering.Order(requestedTargets.Select(ToPolicyTarget));

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var lockedLotsByLotId = new Dictionary<Guid, CloudStackLot>();
        var backingBiotaIdByLotId = new Dictionary<Guid, uint>();

        foreach (var policyTarget in orderedPolicyTargets)
        {
            if (policyTarget.Kind == CloudReservationTargetKind.Item)
            {
                var biotaId = policyTarget.ItemId!.Value;

                // Locking the custody record row serializes every concurrent Reserve attempt for the
                // same biota, which is what makes the exclusivity check below race-free without
                // needing a partial-unique index (INV-001).
                var record = await LockCustodyRecordByBiotaIdAsync(biotaId, cancellationToken);
                if (record is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(
                        $"Biota {biotaId} has no Cloud Custody Record to reserve for withdrawal.");
                }

                if (record.IsStack)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(
                        $"Biota {biotaId} is a stack Cloud Custody Record; reserve its Cloud Stack Lot(s) instead.");
                }

                if (record.OwnerId != ownerId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict($"Biota {biotaId} is not owned by {ownerId}.");
                }
            }
            else
            {
                var lotId = policyTarget.StackLotId!.Value;

                // ADM-004/MKT-204, transaction rule 9: this transaction's first query must itself
                // be a locking read, not a plain one -- see TryWithdrawLotOnceAsync's matching
                // comment for why a plain first read lets MariaDB's REPEATABLE READ fix this
                // transaction's whole consistent-read snapshot before any lock is taken, which
                // would let the mutation-gate check below observe a snapshot from before a
                // concurrent Global Cloud Maintenance entry committed. Locking the lot first
                // (rather than looking up its custody record with a plain read, as before) closes
                // that window; the backing record is then locked from the now-known
                // CustodyRecordId, matching TryWithdrawLotOnceAsync's own lock order. Re-locking a
                // row this same transaction already holds is a harmless no-op in InnoDB, so two lot
                // targets that happen to share one backing stack simply lock it twice rather than
                // needing extra bookkeeping to avoid it.
                var lot = await LockStackLotAsync(lotId, cancellationToken);
                if (lot is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
                }

                var lotRecord = await LockCustodyRecordAsync(lot.CustodyRecordId, cancellationToken);
                if (lotRecord is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
                }

                if (lot.OwnerId != ownerId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict($"Cloud Stack Lot {lotId} is not owned by {ownerId}.");
                }

                lockedLotsByLotId[lotId] = lot;
                backingBiotaIdByLotId[lotId] = lotRecord.BiotaId;
            }
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterLocks);

        // Build the exclusivity map from every currently active target row (any reservation, any
        // kind) that names one of the requested biotas/lots.
        var requestedBiotaIds = orderedPolicyTargets
            .Where(t => t.Kind == CloudReservationTargetKind.Item)
            .Select(t => t.ItemId!.Value)
            .ToList();
        var requestedLotIds = orderedPolicyTargets
            .Where(t => t.Kind == CloudReservationTargetKind.StackLot)
            .Select(t => t.StackLotId!.Value)
            .ToList();

        var activeConflicts = await (
            from t in _context.CloudWithdrawalReservationTargets.AsNoTracking()
            join r in _context.CloudWithdrawalReservations.AsNoTracking() on t.ReservationId equals r.Id
            where r.Status == CloudReservationStatus.Active
                && ((t.Kind == CloudWithdrawalReservationTargetKind.Item && requestedBiotaIds.Contains(t.ItemBiotaId!.Value))
                    || (t.Kind == CloudWithdrawalReservationTargetKind.StackLot && requestedLotIds.Contains(t.StackLotId!.Value)))
            select new { t.Kind, t.ItemBiotaId, t.StackLotId, r.Id })
            .ToListAsync(cancellationToken);

        var existingAllocationsByTarget = new Dictionary<CloudReservationTarget, CloudReservationAllocation>();
        foreach (var conflict in activeConflicts)
        {
            var conflictTarget = conflict.Kind == CloudWithdrawalReservationTargetKind.Item
                ? CloudReservationTarget.ForItem(new CloudItemId(conflict.ItemBiotaId!.Value))
                : CloudReservationTarget.ForStackLot(new CloudStackLotId(conflict.StackLotId!.Value));

            existingAllocationsByTarget[conflictTarget] = new CloudReservationAllocation(
                new CloudReservationId(conflict.Id), conflictTarget, CloudReservationKind.Withdrawal, CloudReservationStatus.Active);
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        var gateState = await CloudMutationGateReader.ResolveAsync(_context, shardId, cancellationToken);
        var policyResult = CloudReservationPolicy.Open(
            new CloudReservationId(Guid.NewGuid()),
            CloudReservationKind.Withdrawal,
            new CloudAccountId(ownerId),
            orderedPolicyTargets,
            existingAllocationsByTarget,
            new DateTimeOffset(nowUtc, TimeSpan.Zero),
            gateState,
            timeToLive);

        if (!policyResult.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(policyResult.Reason!);
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        var reservation = CloudWithdrawalReservation.Open(
            shardId, ownerId, tokenHash, idempotencyKey,
            policyResult.Reservation!.CreatedAtUtc.UtcDateTime, policyResult.Reservation!.ExpiresAtUtc!.Value.UtcDateTime);
        _context.CloudWithdrawalReservations.Add(reservation);

        var targetRows = new List<CloudWithdrawalReservationTarget>(orderedPolicyTargets.Count);
        foreach (var policyTarget in orderedPolicyTargets)
        {
            targetRows.Add(policyTarget.Kind == CloudReservationTargetKind.Item
                ? CloudWithdrawalReservationTarget.ForItem(reservation.Id, policyTarget.ItemId!.Value)
                : CloudWithdrawalReservationTarget.ForStackLot(
                    reservation.Id, policyTarget.StackLotId!.Value, lockedLotsByLotId[policyTarget.StackLotId!.Value].Quantity));
        }
        _context.CloudWithdrawalReservationTargets.AddRange(targetRows);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);

            var winner = await _context.CloudWithdrawalReservations.AsNoTracking()
                .SingleOrDefaultAsync(r => r.OpenIdempotencyKey == idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(winner);
            }

            return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(
                "One or more requested targets, or this Withdrawal Token, already have an active Withdrawal Reservation.");
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

        var correlationId = Guid.NewGuid();
        foreach (var targetRow in targetRows)
        {
            var biotaId = targetRow.Kind == CloudWithdrawalReservationTargetKind.Item
                ? targetRow.ItemBiotaId!.Value
                : backingBiotaIdByLotId[targetRow.StackLotId!.Value];

            await AppendLedgerAndOutboxAsync(
                correlationId, shardId, CloudBoundaryOperationType.WithdrawalReservationOpened, biotaId, ownerId, faultInjector, cancellationToken);
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(reservation);
    }

    private async Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> TryCancelWithdrawalReservationOnceAsync(
        Guid reservationId, int expectedVersion, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var incompatible = await CheckProtocolCompatibilityAsync<CloudWithdrawalReservation>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var reservation = await LockWithdrawalReservationAsync(reservationId, cancellationToken);
        if (reservation is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict($"Withdrawal Reservation {reservationId} does not exist.");
        }

        if (reservation.Status == CloudReservationStatus.Released)
        {
            await transaction.RollbackAsync(cancellationToken);

            if (reservation.ReleaseReason == CloudReservationReleaseReason.Cancelled)
            {
                return CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(reservation);
            }

            return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(
                $"Withdrawal Reservation {reservationId} was already released ({reservation.ReleaseReason}) and cannot be cancelled.");
        }

        if (reservation.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(
                $"Withdrawal Reservation {reservationId} is at version {reservation.Version}, not the expected version {expectedVersion}.");
        }

        var targets = await _context.CloudWithdrawalReservationTargets.AsNoTracking()
            .Where(t => t.ReservationId == reservationId)
            .ToListAsync(cancellationToken);

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        reservation.Release(nowUtc, CloudReservationReleaseReason.Cancelled);
        _context.CloudWithdrawalReservations.Update(reservation);
        await _context.SaveChangesAsync(cancellationToken);

        var correlationId = Guid.NewGuid();
        foreach (var target in targets)
        {
            var biotaId = await ResolveTargetBackingBiotaIdAsync(target, cancellationToken);
            await AppendLedgerAndOutboxAsync(
                correlationId, reservation.ShardId, CloudBoundaryOperationType.WithdrawalReservationCancelled,
                biotaId, reservation.OwnerId, faultInjector: null, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(reservation);
    }

    private async Task<CloudBoundaryOutcome<CloudMultiWithdrawalResult>> TryRedeemWithdrawalReservationOnceAsync(
        string tokenHash,
        uint recipientContainerId,
        IReadOnlyDictionary<Guid, uint> materializedBiotaIdsByTargetId,
        Guid idempotencyKey,
        Func<CloudBoundaryFaultPoint, Task>? faultInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeLocks);

        var incompatible = await CheckProtocolCompatibilityAsync<CloudMultiWithdrawalResult>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existingIdempotency = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existingIdempotency is not null)
        {
            return await ReplayWithdrawalReservationRedemptionAsync(existingIdempotency, cancellationToken);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var reservation = await LockWithdrawalReservationByTokenHashAsync(tokenHash, cancellationToken);
        if (reservation is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            var winner = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return await ReplayWithdrawalReservationRedemptionAsync(winner, cancellationToken);
            }

            return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Conflict("No Withdrawal Reservation matches this Withdrawal Token.");
        }

        if (reservation.Status != CloudReservationStatus.Active)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Conflict(
                $"Withdrawal Reservation {reservation.Id} is not active ({reservation.ReleaseReason}); its Withdrawal Token cannot be redeemed.");
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        if (reservation.IsExpiredAt(nowUtc))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Conflict(
                $"Withdrawal Reservation {reservation.Id} expired at {reservation.ExpiresAtUtc:O} and cannot be redeemed.");
        }

        // ADM-004/MKT-204, transaction rule 9: revalidated at the exact instant this reservation is
        // locked, not only earlier in the request pipeline.
        var frozen = await CheckMutationGateAsync<CloudMultiWithdrawalResult>(reservation.ShardId, cancellationToken);
        if (frozen is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return frozen;
        }

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterValidation);

        var targets = await _context.CloudWithdrawalReservationTargets.AsNoTracking()
            .Where(t => t.ReservationId == reservation.Id)
            .ToListAsync(cancellationToken);

        // Deterministic multi-target lock order (transaction rule 2), the exact same order
        // ReserveForWithdrawalAsync locked them in.
        var targetsByPolicyTarget = targets.ToDictionary(t => t.ToPolicyTarget());
        var orderedPolicyTargets = CloudReservationTargetOrdering.Order(targetsByPolicyTarget.Keys);

        var shardId = reservation.ShardId;
        var ownerId = reservation.OwnerId;
        var correlationId = Guid.NewGuid();
        var deliveries = new List<CloudWithdrawalDeliveryItem>(targets.Count);

        foreach (var policyTarget in orderedPolicyTargets)
        {
            var target = targetsByPolicyTarget[policyTarget];

            if (target.Kind == CloudWithdrawalReservationTargetKind.Item)
            {
                var record = await LockCustodyRecordByBiotaIdAsync(target.ItemBiotaId!.Value, cancellationToken);
                if (record is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Conflict(
                        $"Biota {target.ItemBiotaId} no longer has a Cloud Custody Record to withdraw.");
                }

                await ReleaseCustodyRecordAsync(record, cancellationToken);
                await _context.SaveChangesAsync(cancellationToken);
                await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

                await GrantContainerAsync(record.BiotaId, recipientContainerId, cancellationToken);
                await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

                await AppendLedgerAndOutboxAsync(
                    correlationId, shardId, CloudBoundaryOperationType.Withdrawal, record.BiotaId, ownerId, faultInjector, cancellationToken);

                deliveries.Add(new CloudWithdrawalDeliveryItem(record.BiotaId, Quantity: null));
            }
            else
            {
                var lotId = target.StackLotId!.Value;

                var custodyRecordId = await _context.CloudStackLots.AsNoTracking()
                    .Where(l => l.Id == lotId)
                    .Select(l => (Guid?)l.CustodyRecordId)
                    .SingleOrDefaultAsync(cancellationToken);
                if (custodyRecordId is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Conflict($"Cloud Stack Lot {lotId} no longer exists to withdraw.");
                }

                // Deterministic lock order (transaction rule 2): the backing stack record before the lot.
                var record = await LockCustodyRecordAsync(custodyRecordId.Value, cancellationToken);
                var lot = await LockStackLotAsync(lotId, cancellationToken);

                if (record is null || lot is null || lot.CustodyRecordId != record.Id)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Conflict($"Cloud Stack Lot {lotId} no longer exists to withdraw.");
                }

                // Defense in depth: never trust the quantity captured when the reservation was
                // opened for what to actually deliver -- re-derive it from the lot this transaction
                // just locked.
                if (lot.Quantity != target.Quantity)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Conflict(
                        $"Cloud Stack Lot {lotId} quantity changed from {target.Quantity} to {lot.Quantity} since its Withdrawal "
                            + "Reservation was opened; this reservation can no longer be redeemed safely.");
                }

                var quantityToWithdraw = target.Quantity!.Value;
                var siblingCount = await _context.CloudStackLots
                    .CountAsync(l => l.CustodyRecordId == record.Id && l.Id != lot.Id, cancellationToken);
                var isFullStackWithdrawal = siblingCount == 0;

                if (!isFullStackWithdrawal && !materializedBiotaIdsByTargetId.TryGetValue(target.Id, out var materializedBiotaId))
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Conflict(
                        $"A materialized child GUID (allocated by ACE) is required to redeem Cloud Stack Lot {lotId}, which is not "
                            + "the sole lot on its stack.");
                }

                var originalBiotaId = record.BiotaId;
                uint deliveredBiotaId;

                if (isFullStackWithdrawal)
                {
                    _context.CloudStackLots.Remove(lot);
                    await ReleaseCustodyRecordAsync(record, cancellationToken);
                    await _context.SaveChangesAsync(cancellationToken);
                    await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

                    await GrantContainerAsync(originalBiotaId, recipientContainerId, cancellationToken);
                    await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

                    deliveredBiotaId = originalBiotaId;
                }
                else
                {
                    var materializedBiotaId2 = materializedBiotaIdsByTargetId[target.Id];

                    _context.CloudStackLots.Remove(lot);
                    record.ReduceStackTotalQuantity(quantityToWithdraw);
                    _context.CloudCustodyRecords.Update(record);
                    await _context.SaveChangesAsync(cancellationToken);
                    await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCustodyChange);

                    await MaterializeChildBiotaAsync(originalBiotaId, materializedBiotaId2, quantityToWithdraw, cancellationToken);
                    await UpsertStackSizeAsync(originalBiotaId, record.TotalQuantity!.Value, cancellationToken);
                    await GrantContainerAsync(materializedBiotaId2, recipientContainerId, cancellationToken);
                    await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterPossessionChange);

                    deliveredBiotaId = materializedBiotaId2;

                    _context.CloudStackLotLineageEvents.Add(
                        new CloudStackLotLineageEvent(correlationId, shardId, originalBiotaId, materializedBiotaId2, quantityToWithdraw, ownerId));
                    await _context.SaveChangesAsync(cancellationToken);
                }

                await AppendLedgerAndOutboxAsync(
                    correlationId, shardId, CloudBoundaryOperationType.StackWithdrawal, deliveredBiotaId, ownerId, faultInjector, cancellationToken);

                deliveries.Add(new CloudWithdrawalDeliveryItem(deliveredBiotaId, quantityToWithdraw));
            }
        }

        reservation.Release(nowUtc, CloudReservationReleaseReason.Fulfilled);
        _context.CloudWithdrawalReservations.Update(reservation);
        await _context.SaveChangesAsync(cancellationToken);

        // CloudIdempotencyRecord requires *a* representative non-zero biota GUID; the complete
        // ordered delivery list -- the actual replay contract -- lives in the
        // CloudWithdrawalRedemptionDeliveryItem rows added below.
        _context.CloudIdempotencyRecords.Add(
            new CloudIdempotencyRecord(
                idempotencyKey, shardId, CloudBoundaryOperationType.WithdrawalReservationRedeemed, deliveries[0].DeliveredBiotaId, ownerId,
                custodyRecordId: null, targetContainerId: recipientContainerId, correlationId));
        await _context.SaveChangesAsync(cancellationToken);

        _context.CloudWithdrawalRedemptionDeliveryItems.AddRange(
            deliveries.Select((delivery, index) =>
                new CloudWithdrawalRedemptionDeliveryItem(idempotencyKey, index, delivery.DeliveredBiotaId, delivery.Quantity)));
        await _context.SaveChangesAsync(cancellationToken);

        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.BeforeCommit);
        await transaction.CommitAsync(cancellationToken);
        await InvokeFaultInjectorAsync(faultInjector, CloudBoundaryFaultPoint.AfterCommit);

        return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Committed(
            new CloudMultiWithdrawalResult(deliveries, recipientContainerId, ownerId));
    }

    private async Task<CloudBoundaryOutcome<CloudMultiWithdrawalResult>> ReplayWithdrawalReservationRedemptionAsync(
        CloudIdempotencyRecord existing, CancellationToken cancellationToken)
    {
        if (existing.OperationType != CloudBoundaryOperationType.WithdrawalReservationRedeemed)
        {
            return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not a WithdrawalReservationRedeemed.");
        }

        var deliveryRows = await _context.CloudWithdrawalRedemptionDeliveryItems.AsNoTracking()
            .Where(d => d.RedemptionIdempotencyKey == existing.IdempotencyKey)
            .OrderBy(d => d.OrdinalPosition)
            .ToListAsync(cancellationToken);

        if (deliveryRows.Count == 0)
        {
            // ARCH-006 commits the idempotency record and every delivery row in the same
            // transaction, so a committed redemption whose delivery rows are gone is not a normal
            // conflict -- it means that invariant was broken out of band.
            throw new CloudCustodyConflictException(
                $"Idempotency key {existing.IdempotencyKey} committed a Withdrawal Reservation redemption whose delivery rows no longer exist.");
        }

        var deliveries = deliveryRows
            .Select(d => new CloudWithdrawalDeliveryItem(d.DeliveredBiotaId, d.Quantity))
            .ToList();

        return CloudBoundaryOutcome<CloudMultiWithdrawalResult>.Committed(
            new CloudMultiWithdrawalResult(deliveries, existing.TargetContainerId!.Value, existing.OwnerId));
    }

    private static CloudReservationTarget ToPolicyTarget(CloudWithdrawalReservationRequestTarget requestTarget) => requestTarget.Kind switch
    {
        CloudWithdrawalReservationTargetKind.Item => CloudReservationTarget.ForItem(new CloudItemId(requestTarget.ItemBiotaId)),
        CloudWithdrawalReservationTargetKind.StackLot => CloudReservationTarget.ForStackLot(new CloudStackLotId(requestTarget.StackLotId)),
        _ => throw new ArgumentOutOfRangeException(nameof(requestTarget), "Unrecognized Cloud Withdrawal Reservation request target kind."),
    };

    private async Task<uint> ResolveTargetBackingBiotaIdAsync(CloudWithdrawalReservationTarget target, CancellationToken cancellationToken)
    {
        if (target.Kind == CloudWithdrawalReservationTargetKind.Item)
        {
            return target.ItemBiotaId!.Value;
        }

        var custodyRecordId = await _context.CloudStackLots.AsNoTracking()
            .Where(l => l.Id == target.StackLotId!.Value)
            .Select(l => (Guid?)l.CustodyRecordId)
            .SingleOrDefaultAsync(cancellationToken);

        return await ResolveBackingBiotaIdAsync(custodyRecordId, cancellationToken);
    }

    /// <summary>
    /// Resolves a Cloud Stack Lot's backing biota GUID for ledger/outbox display. By the time any
    /// caller reaches this, the lot's own row lock (Reserve) or its still-Active reservation (Cancel)
    /// already guarantees the backing <see cref="CloudCustodyRecord"/> exists --
    /// <see cref="CloudStackLot.CustodyRecordId"/>'s foreign key makes it impossible for that row to
    /// be missing. A null <paramref name="custodyRecordId"/> or missing record therefore means an
    /// out-of-band integrity violation, not a normal race; this method still fails closed with an
    /// explicit exception rather than silently recording a bogus ledger entry.
    /// </summary>
    private async Task<uint> ResolveBackingBiotaIdAsync(Guid? custodyRecordId, CancellationToken cancellationToken)
    {
        if (custodyRecordId is null)
        {
            throw new CloudCustodyConflictException("A Cloud Stack Lot's backing Cloud Custody Record could not be resolved despite its foreign key guarantee.");
        }

        var biotaId = await _context.CloudCustodyRecords.AsNoTracking()
            .Where(r => r.Id == custodyRecordId.Value)
            .Select(r => (uint?)r.BiotaId)
            .SingleOrDefaultAsync(cancellationToken);

        return biotaId ?? throw new CloudCustodyConflictException(
            $"Cloud Custody Record {custodyRecordId.Value} could not be resolved despite its foreign key guarantee.");
    }

    private async Task<CloudCustodyRecord?> LockCustodyRecordByBiotaIdAsync(uint biotaId, CancellationToken cancellationToken) =>
        await _context.CloudCustodyRecords
            .FromSqlInterpolated($"SELECT * FROM CloudCustodyRecord WHERE BiotaId = {biotaId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudWithdrawalReservation?> LockWithdrawalReservationAsync(Guid reservationId, CancellationToken cancellationToken) =>
        await _context.CloudWithdrawalReservations
            .FromSqlInterpolated($"SELECT * FROM CloudWithdrawalReservation WHERE Id = {reservationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudWithdrawalReservation?> LockWithdrawalReservationByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        await _context.CloudWithdrawalReservations
            .FromSqlInterpolated($"SELECT * FROM CloudWithdrawalReservation WHERE TokenHash = {tokenHash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
}
