using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Issue #16's Cloud Stack Lot withdrawal reservation half of the World Boundary Authority (WDR-001,
/// WDR-002, WDR-003, WDR-008, INV-002, INV-003, ARCH-002, ARCH-006). Mirrors
/// <see cref="CloudCustodyBoundary"/>'s whole-item Withdrawal Reservation methods exactly (open,
/// cancel, redeem, idempotent replay, commit-time revalidation under a held row lock), but targets an
/// entire <see cref="CloudStackLot"/> as its exclusive unit -- the same granularity
/// <see cref="CloudReservationTarget.ForStackLot"/> already models -- rather than a whole biota.
///
/// A reservation always covers one whole lot's current quantity; a caller who wants to reserve fewer
/// than a stack's full quantity must first split off a new lot for exactly that quantity through
/// <see cref="CloudStackLotTransactionAuthority.SplitLotAsync"/> (a pure Cloud-schema operation that
/// works even while ACE is offline, ADR-0002) and then reserve that new lot. Redemption reuses the
/// exact materialize-or-deliver-original branching <see cref="WithdrawLotAsync"/> already proved
/// (INV-003): delivering the original biota when the reserved lot is the only lot left on its stack,
/// or materializing a child under a caller-supplied (ACE-allocated) GUID otherwise.
/// </summary>
public sealed partial class CloudCustodyBoundary
{
    /// <summary>
    /// Opens ACE's local authority record for a new Withdrawal Token's exclusive reservation over an
    /// entire Cloud Stack Lot (WDR-001, WDR-002, INV-002). Reuses
    /// <see cref="CloudReservationPolicy.Open"/> for the exclusivity decision, exactly like
    /// <see cref="ReserveForWithdrawalAsync"/>. Repeating this call with the same
    /// <paramref name="idempotencyKey"/> replays the original committed reservation (transaction
    /// rule 4).
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>> ReserveStackLotForWithdrawalAsync(
        Guid lotId,
        string shardId,
        Guid ownerId,
        string tokenHash,
        TimeSpan timeToLive,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        RequireIdempotencyKey(idempotencyKey);

        if (lotId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Stack Lot Withdrawal Reservation requires a real Cloud Stack Lot ID.", nameof(lotId));
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
            () => TryReserveStackLotForWithdrawalOnceAsync(lotId, shardId, ownerId, tokenHash, timeToLive, idempotencyKey, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a reservation open previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet (transaction rule 8).
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>?> TryGetStackLotWithdrawalReservationOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CloudStackLotWithdrawalReservations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OpenIdempotencyKey == idempotencyKey, cancellationToken);
        return existing is null ? null : CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Committed(existing);
    }

    /// <summary>
    /// Cancels an active Cloud Stack Lot Withdrawal Reservation before redemption (WDR-003).
    /// Idempotent by construction, exactly like <see cref="CancelWithdrawalReservationAsync"/>.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>> CancelStackLotWithdrawalReservationAsync(
        Guid reservationId, int expectedVersion, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(
            () => TryCancelStackLotWithdrawalReservationOnceAsync(reservationId, expectedVersion, cancellationToken),
            cancellationToken: cancellationToken);

    /// <summary>
    /// Reads whether a Withdrawal Token's local Cloud Stack Lot reservation is currently active
    /// without consuming it (WDR-008). Returns null when no active reservation matches
    /// <paramref name="tokenHash"/>.
    /// </summary>
    public async Task<CloudStackLotWithdrawalReservation?> TryGetActiveStackLotWithdrawalReservationAsync(
        string tokenHash, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Looking up a Withdrawal Reservation requires its Withdrawal Token hash.", nameof(tokenHash));
        }

        return await _context.CloudStackLotWithdrawalReservations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.TokenHash == tokenHash && r.Status == CloudReservationStatus.Active, cancellationToken);
    }

    /// <summary>
    /// Informational, unlocked read (not itself a commit-time revalidation -- <see cref="RedeemStackLotWithdrawalReservationAsync"/>
    /// re-derives this fact fresh under its own row lock) that an ACE-side caller uses to decide
    /// whether it needs to pre-allocate a materialized child GUID before calling redeem at all
    /// (ARCH-010: only ACE may allocate that GUID, and only ACE's own GuidManager can do so, which
    /// this pure-data-access project has no way to reach). Returns null if the lot no longer exists.
    /// </summary>
    public async Task<CloudStackLotWithdrawalPreview?> PreviewStackLotWithdrawalAsync(Guid lotId, CancellationToken cancellationToken = default)
    {
        var lot = await _context.CloudStackLots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == lotId, cancellationToken);
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

        return new CloudStackLotWithdrawalPreview(record.BiotaId, siblingCount == 0);
    }

    /// <summary>
    /// Redeems a Withdrawal Token whose reservation targets a Cloud Stack Lot: atomically performs
    /// the same materialize-or-deliver-original transition <see cref="WithdrawLotAsync"/> proves
    /// (INV-003) and releases the reservation as fulfilled, in one transaction (WDR-001, WDR-003).
    /// Refuses an expired or already-released reservation instead of redeeming it. Repeating this
    /// call with the same <paramref name="idempotencyKey"/> replays the original committed result
    /// (transaction rule 4). <paramref name="materializedBiotaId"/> must be supplied (ACE-allocated,
    /// ARCH-010) whenever the reserved lot is not the sole lot backing its stack; passing null when
    /// one is required refuses with a Conflict rather than guessing.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudStackWithdrawalResult>> RedeemStackLotWithdrawalReservationAsync(
        string tokenHash,
        uint recipientContainerId,
        uint? materializedBiotaId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
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

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryRedeemStackLotWithdrawalReservationOnceAsync(tokenHash, recipientContainerId, materializedBiotaId, idempotencyKey, cancellationToken),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Returns the committed result of a reservation redemption previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet (transaction rule 8).
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudStackWithdrawalResult>?> TryGetStackLotWithdrawalRedemptionOutcomeAsync(
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        return existing is null ? null : ReplayStackLotWithdrawalReservationRedemption(existing);
    }

    private async Task<CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>> TryReserveStackLotForWithdrawalOnceAsync(
        Guid lotId, string shardId, Guid ownerId, string tokenHash, TimeSpan timeToLive, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var incompatible = await CheckProtocolCompatibilityAsync<CloudStackLotWithdrawalReservation>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existingByKey = await _context.CloudStackLotWithdrawalReservations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OpenIdempotencyKey == idempotencyKey, cancellationToken);
        if (existingByKey is not null)
        {
            return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Committed(existingByKey);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var custodyRecordId = await _context.CloudStackLots.AsNoTracking()
            .Where(l => l.Id == lotId)
            .Select(l => (Guid?)l.CustodyRecordId)
            .SingleOrDefaultAsync(cancellationToken);

        if (custodyRecordId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
        }

        // Deterministic lock order (transaction rule 2): the backing stack record before the lot,
        // matching every other stack-mutating operation in this class.
        await LockCustodyRecordAsync(custodyRecordId.Value, cancellationToken);
        var lot = await LockStackLotAsync(lotId, cancellationToken);

        if (lot is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
        }

        if (lot.OwnerId != ownerId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Conflict($"Cloud Stack Lot {lotId} is not owned by {ownerId}.");
        }

        var activeReservation = await _context.CloudStackLotWithdrawalReservations.AsNoTracking()
            .Where(r => r.LotId == lotId && r.Status == CloudReservationStatus.Active)
            .SingleOrDefaultAsync(cancellationToken);

        var target = CloudReservationTarget.ForStackLot(new CloudStackLotId(lotId));
        var existingAllocationsByTarget = new Dictionary<CloudReservationTarget, CloudReservationAllocation>();
        if (activeReservation is not null)
        {
            existingAllocationsByTarget[target] = new CloudReservationAllocation(
                new CloudReservationId(activeReservation.Id), target, CloudReservationKind.Withdrawal, CloudReservationStatus.Active);
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        var policyResult = CloudReservationPolicy.Open(
            new CloudReservationId(Guid.NewGuid()),
            CloudReservationKind.Withdrawal,
            new CloudAccountId(ownerId),
            [target],
            existingAllocationsByTarget,
            new DateTimeOffset(nowUtc, TimeSpan.Zero),
            CloudMutationGateState.Open,
            timeToLive);

        if (!policyResult.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Conflict(policyResult.Reason!);
        }

        var reservation = CloudStackLotWithdrawalReservation.Open(
            shardId, lotId, lot.Quantity, ownerId, tokenHash, idempotencyKey,
            policyResult.Reservation!.CreatedAtUtc.UtcDateTime, policyResult.Reservation!.ExpiresAtUtc!.Value.UtcDateTime);
        _context.CloudStackLotWithdrawalReservations.Add(reservation);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);

            var winner = await _context.CloudStackLotWithdrawalReservations.AsNoTracking()
                .SingleOrDefaultAsync(r => r.OpenIdempotencyKey == idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Committed(winner);
            }

            return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Conflict(
                $"A Withdrawal Reservation for Cloud Stack Lot {lotId} or this Withdrawal Token already exists.");
        }

        var correlationId = Guid.NewGuid();
        var backingBiotaId = await ResolveBackingBiotaIdAsync(lot.CustodyRecordId, cancellationToken);
        await AppendLedgerAndOutboxAsync(
            correlationId, shardId, CloudBoundaryOperationType.StackLotReservationOpened, backingBiotaId, ownerId,
            faultInjector: null, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Committed(reservation);
    }

    private async Task<CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>> TryCancelStackLotWithdrawalReservationOnceAsync(
        Guid reservationId, int expectedVersion, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var incompatible = await CheckProtocolCompatibilityAsync<CloudStackLotWithdrawalReservation>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var reservation = await LockStackLotWithdrawalReservationAsync(reservationId, cancellationToken);
        if (reservation is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Conflict($"Withdrawal Reservation {reservationId} does not exist.");
        }

        if (reservation.Status == CloudReservationStatus.Released)
        {
            await transaction.RollbackAsync(cancellationToken);

            if (reservation.ReleaseReason == CloudReservationReleaseReason.Cancelled)
            {
                return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Committed(reservation);
            }

            return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Conflict(
                $"Withdrawal Reservation {reservationId} was already released ({reservation.ReleaseReason}) and cannot be cancelled.");
        }

        if (reservation.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Conflict(
                $"Withdrawal Reservation {reservationId} is at version {reservation.Version}, not the expected version {expectedVersion}.");
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        reservation.Release(nowUtc, CloudReservationReleaseReason.Cancelled);
        _context.CloudStackLotWithdrawalReservations.Update(reservation);
        await _context.SaveChangesAsync(cancellationToken);

        var correlationId = Guid.NewGuid();
        var custodyRecordId = await _context.CloudStackLots.AsNoTracking()
            .Where(l => l.Id == reservation.LotId)
            .Select(l => (Guid?)l.CustodyRecordId)
            .SingleOrDefaultAsync(cancellationToken);
        await AppendLedgerAndOutboxAsync(
            correlationId, reservation.ShardId, CloudBoundaryOperationType.StackLotReservationCancelled,
            await ResolveBackingBiotaIdAsync(custodyRecordId, cancellationToken), reservation.OwnerId, faultInjector: null, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudStackLotWithdrawalReservation>.Committed(reservation);
    }

    private async Task<CloudBoundaryOutcome<CloudStackWithdrawalResult>> TryRedeemStackLotWithdrawalReservationOnceAsync(
        string tokenHash, uint recipientContainerId, uint? materializedBiotaId, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var incompatible = await CheckProtocolCompatibilityAsync<CloudStackWithdrawalResult>(cancellationToken);
        if (incompatible is not null)
        {
            return incompatible;
        }

        var existingIdempotency = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existingIdempotency is not null)
        {
            return ReplayStackLotWithdrawalReservationRedemption(existingIdempotency);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var reservation = await LockStackLotWithdrawalReservationByTokenHashAsync(tokenHash, cancellationToken);
        if (reservation is null)
        {
            await transaction.RollbackAsync(cancellationToken);

            var winner = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return ReplayStackLotWithdrawalReservationRedemption(winner);
            }

            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict("No Withdrawal Reservation matches this Withdrawal Token.");
        }

        if (reservation.Status != CloudReservationStatus.Active)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                $"Withdrawal Reservation {reservation.Id} is not active ({reservation.ReleaseReason}); its Withdrawal Token cannot be redeemed.");
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        if (reservation.IsExpiredAt(nowUtc))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                $"Withdrawal Reservation {reservation.Id} expired at {reservation.ExpiresAtUtc:O} and cannot be redeemed.");
        }

        var custodyRecordId = await _context.CloudStackLots.AsNoTracking()
            .Where(l => l.Id == reservation.LotId)
            .Select(l => (Guid?)l.CustodyRecordId)
            .SingleOrDefaultAsync(cancellationToken);

        if (custodyRecordId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                $"Cloud Stack Lot {reservation.LotId} no longer exists to withdraw.");
        }

        // Deterministic lock order (transaction rule 2): the backing stack record before the lot.
        var record = await LockCustodyRecordAsync(custodyRecordId.Value, cancellationToken);
        var lot = await LockStackLotAsync(reservation.LotId, cancellationToken);

        if (record is null || lot is null || lot.CustodyRecordId != record.Id)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                $"Cloud Stack Lot {reservation.LotId} no longer exists to withdraw.");
        }

        var quantityToWithdraw = reservation.Quantity;
        var siblingCount = await _context.CloudStackLots
            .CountAsync(l => l.CustodyRecordId == record.Id && l.Id != lot.Id, cancellationToken);
        var isFullStackWithdrawal = siblingCount == 0;

        if (!isFullStackWithdrawal && (materializedBiotaId is null or 0))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                "A materialized child GUID (allocated by ACE) is required to redeem a reservation that is not the sole lot on its stack.");
        }

        var originalBiotaId = record.BiotaId;
        var shardId = record.ShardId;
        var ownerId = reservation.OwnerId;
        var correlationId = Guid.NewGuid();
        uint deliveredBiotaId;

        if (isFullStackWithdrawal)
        {
            _context.CloudStackLots.Remove(lot);
            await ReleaseCustodyRecordAsync(record, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);

            await GrantContainerAsync(originalBiotaId, recipientContainerId, cancellationToken);

            deliveredBiotaId = originalBiotaId;
        }
        else
        {
            _context.CloudStackLots.Remove(lot);
            record.ReduceStackTotalQuantity(quantityToWithdraw);
            _context.CloudCustodyRecords.Update(record);
            await _context.SaveChangesAsync(cancellationToken);

            await MaterializeChildBiotaAsync(originalBiotaId, materializedBiotaId!.Value, quantityToWithdraw, cancellationToken);
            await UpsertStackSizeAsync(originalBiotaId, record.TotalQuantity!.Value, cancellationToken);
            await GrantContainerAsync(materializedBiotaId.Value, recipientContainerId, cancellationToken);

            deliveredBiotaId = materializedBiotaId.Value;

            _context.CloudStackLotLineageEvents.Add(
                new CloudStackLotLineageEvent(correlationId, shardId, originalBiotaId, materializedBiotaId.Value, quantityToWithdraw, ownerId));
            await _context.SaveChangesAsync(cancellationToken);
        }

        await AppendLedgerAndOutboxAsync(
            correlationId, shardId, CloudBoundaryOperationType.StackLotReservationRedeemed, deliveredBiotaId, ownerId, faultInjector: null, cancellationToken);

        reservation.Release(nowUtc, CloudReservationReleaseReason.Fulfilled);
        _context.CloudStackLotWithdrawalReservations.Update(reservation);
        await _context.SaveChangesAsync(cancellationToken);

        _context.CloudIdempotencyRecords.Add(
            new CloudIdempotencyRecord(
                idempotencyKey, shardId, CloudBoundaryOperationType.StackLotReservationRedeemed, deliveredBiotaId, ownerId,
                custodyRecordId: record.Id, targetContainerId: recipientContainerId, correlationId, quantityToWithdraw));
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Committed(
            new CloudStackWithdrawalResult(deliveredBiotaId, recipientContainerId, ownerId, quantityToWithdraw));
    }

    private static CloudBoundaryOutcome<CloudStackWithdrawalResult> ReplayStackLotWithdrawalReservationRedemption(CloudIdempotencyRecord existing)
    {
        if (existing.OperationType != CloudBoundaryOperationType.StackLotReservationRedeemed)
        {
            return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not a StackLotReservationRedeemed.");
        }

        return CloudBoundaryOutcome<CloudStackWithdrawalResult>.Committed(
            new CloudStackWithdrawalResult(existing.BiotaId, existing.TargetContainerId!.Value, existing.OwnerId, existing.Quantity!.Value));
    }

    private async Task<CloudStackLotWithdrawalReservation?> LockStackLotWithdrawalReservationAsync(Guid reservationId, CancellationToken cancellationToken) =>
        await _context.CloudStackLotWithdrawalReservations
            .FromSqlInterpolated($"SELECT * FROM CloudStackLotWithdrawalReservation WHERE Id = {reservationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudStackLotWithdrawalReservation?> LockStackLotWithdrawalReservationByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        await _context.CloudStackLotWithdrawalReservations
            .FromSqlInterpolated($"SELECT * FROM CloudStackLotWithdrawalReservation WHERE TokenHash = {tokenHash} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Resolves the lot's backing biota GUID for ledger/outbox display (Reserve/Cancel only ever
    /// touch the lot itself, never the biota row). By the time either caller reaches this, the lot's
    /// own row lock (Reserve) or its still-Active reservation (Cancel) already guarantees the backing
    /// <see cref="CloudCustodyRecord"/> exists -- <see cref="CloudStackLot.CustodyRecordId"/>'s
    /// foreign key makes it impossible for that row to be missing. A null <paramref name="custodyRecordId"/>
    /// or missing record therefore means an out-of-band integrity violation, not a normal race; this
    /// method still fails closed with an explicit exception (<see cref="CloudActivityLedgerEvent"/>'s
    /// own constructor rejects a zero biota ID) rather than silently recording a bogus ledger entry.
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
}
