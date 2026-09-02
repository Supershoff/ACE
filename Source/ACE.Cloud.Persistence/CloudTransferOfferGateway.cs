using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's own transaction boundary for Transfer Offers (issue #35,
/// XFER-001, XFER-002, INV-002, INV-004..006, EVT-001, EVT-003): locked, idempotent creation plus
/// atomic accept/decline/cancel/expire. Distinct from <see cref="CloudCustodyBoundary"/> (ACE's World
/// Boundary Authority gateway): a Transfer Offer never touches a native biota's Container/Wielder/
/// Location property (ARCH-004) -- acceptance is an ordinary off-world ownership reassignment,
/// mirroring <see cref="CloudAllegianceVaultGateway.AbsorbAsync"/>'s own shape.
///
/// <see cref="CloudTransferOfferPolicy"/> (ACE.Cloud.Domain) is the independently testable pure
/// specification of every rule this class applies; it is not literally called at runtime because
/// <see cref="CloudTransferOffer"/>'s own transition methods are internal to that assembly (mirrors
/// <c>CloudWithdrawalReservation.Release</c>'s established rationale for the same situation). This
/// class instead applies the identical precedence directly against <see cref="CloudTransferOfferRecord"/>.
/// </summary>
public sealed class CloudTransferOfferGateway : ICloudTransferOfferService
{
    /// <summary>XFER-002: "expires after seven days."</summary>
    public static readonly TimeSpan OfferDuration = TimeSpan.FromDays(7);

    private readonly CloudDbContext _context;
    private readonly ICloudAccountOwnershipResolver _ownershipResolver;

    public CloudTransferOfferGateway(CloudDbContext context, ICloudAccountOwnershipResolver ownershipResolver)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _ownershipResolver = ownershipResolver ?? throw new ArgumentNullException(nameof(ownershipResolver));
    }

    /// <summary>
    /// Creates a new Transfer Offer: resolves <paramref name="recipientCharacterName"/> to its
    /// current owning account's effective Main Account exactly once (XFER-001), then opens an
    /// exclusive hold over every requested target or none of them (XFER-002). Repeating this call
    /// with the same <paramref name="idempotencyKey"/> replays the original committed result
    /// (transaction rule 4).
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> CreateAsync(
        string shardId,
        uint senderAccountId,
        string recipientCharacterName,
        IReadOnlyList<CloudTransferOfferRequestTarget> targets,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Transfer Offer requires a Cloud Shard ID.", nameof(shardId));
        }

        if (senderAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(senderAccountId), "A Transfer Offer requires a real sender account ID.");
        }

        if (string.IsNullOrWhiteSpace(recipientCharacterName))
        {
            throw new ArgumentException("A Transfer Offer requires a typed recipient character name.", nameof(recipientCharacterName));
        }

        ArgumentNullException.ThrowIfNull(targets);

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A Transfer Offer requires a non-empty idempotency key.", nameof(idempotencyKey));
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryCreateOnceAsync(shardId, senderAccountId, recipientCharacterName, targets, idempotencyKey, cancellationToken),
            cancellationToken: cancellationToken);
    }

    private async Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> TryCreateOnceAsync(
        string shardId,
        uint senderAccountId,
        string recipientCharacterName,
        IReadOnlyList<CloudTransferOfferRequestTarget> targets,
        Guid idempotencyKey,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var existingByKey = await _context.Set<CloudTransferOfferRecord>().AsNoTracking()
            .SingleOrDefaultAsync(o => o.CreateIdempotencyKey == idempotencyKey, cancellationToken);
        if (existingByKey is not null)
        {
            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Committed(existingByKey);
        }

        var effectiveSenderAccountId = await _ownershipResolver.ResolveEffectiveOwnerAccountIdAsync(shardId, senderAccountId, cancellationToken);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(shardId, effectiveSenderAccountId);

        var (recipientFound, recipientCharacterAccountId) = await TryResolveCurrentCharacterAccountAsync(recipientCharacterName, cancellationToken);
        Guid? recipientOwnerId = null;
        if (recipientFound)
        {
            var effectiveRecipientAccountId =
                await _ownershipResolver.ResolveEffectiveOwnerAccountIdAsync(shardId, recipientCharacterAccountId, cancellationToken);
            recipientOwnerId = CloudOwnerIdentity.ForAccount(shardId, effectiveRecipientAccountId);
        }

        // Deterministic multi-target lock order (transaction rule 2), mirroring
        // CloudCustodyBoundary.TryReserveForWithdrawalOnceAsync's own established shape exactly.
        var orderedPolicyTargets = CloudReservationTargetOrdering.Order(targets.Select(ToPolicyTarget));

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var lockedLotsByLotId = new Dictionary<Guid, CloudStackLot>();
        var backingBiotaIdByLotId = new Dictionary<Guid, uint>();

        foreach (var policyTarget in orderedPolicyTargets)
        {
            if (policyTarget.Kind == CloudReservationTargetKind.Item)
            {
                var biotaId = policyTarget.ItemId!.Value;
                var record = await LockCustodyRecordByBiotaIdAsync(biotaId, cancellationToken);
                if (record is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Biota {biotaId} has no Cloud Custody Record to offer.");
                }

                if (record.IsStack)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict(
                        $"Biota {biotaId} is a stack Cloud Custody Record; offer its Cloud Stack Lot(s) instead.");
                }

                if (record.OwnerId != senderOwnerId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Biota {biotaId} is not owned by the sender.");
                }
            }
            else
            {
                var lotId = policyTarget.StackLotId!.Value;
                var lot = await LockStackLotAsync(lotId, cancellationToken);
                if (lot is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
                }

                var lotRecord = await LockCustodyRecordAsync(lot.CustodyRecordId, cancellationToken);
                if (lotRecord is null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
                }

                if (lot.OwnerId != senderOwnerId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Cloud Stack Lot {lotId} is not owned by the sender.");
                }

                lockedLotsByLotId[lotId] = lot;
                backingBiotaIdByLotId[lotId] = lotRecord.BiotaId;
            }
        }

        // XFER-002/INV-001: a target already exclusively reserved by *any* reservation kind -- an
        // active Withdrawal Reservation or another Pending Transfer Offer alike -- blocks a new
        // offer, exactly like it blocks a new Withdrawal Reservation
        // (CloudCustodyBoundary.TryReserveForWithdrawalOnceAsync's own matching query).
        var requestedBiotaIds = orderedPolicyTargets.Where(t => t.Kind == CloudReservationTargetKind.Item).Select(t => t.ItemId!.Value).ToList();
        var requestedLotIds = orderedPolicyTargets.Where(t => t.Kind == CloudReservationTargetKind.StackLot).Select(t => t.StackLotId!.Value).ToList();

        var existingAllocationsByTarget = await BuildExistingAllocationsAsync(requestedBiotaIds, requestedLotIds, cancellationToken);

        var recipientCurrentProjectedCount = 0;
        int? recipientQuotaLimit = null;
        if (recipientOwnerId is not null)
        {
            await LockOwnerQuotaRowAsync(shardId, recipientOwnerId.Value, cancellationToken);
            recipientQuotaLimit = await CloudStorageQuotaReader.GetPersonalLimitAsync(_context, shardId, cancellationToken);
            recipientCurrentProjectedCount = await CloudStackQuotaProjection.CountProjectedItemsAsync(_context, shardId, recipientOwnerId.Value, cancellationToken);
        }

        var gateState = await CloudMutationGateReader.ResolveAsync(_context, shardId, cancellationToken);
        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);

        var offerId = new CloudTransferOfferId(Guid.NewGuid());
        var request = new CloudTransferOfferCreateRequest(
            new CloudAccountId(senderOwnerId),
            recipientFound,
            recipientOwnerId is null ? null : new CloudAccountId(recipientOwnerId.Value),
            recipientIsCrossShard: false,
            orderedPolicyTargets,
            existingAllocationsByTarget,
            recipientCurrentProjectedCount,
            recipientQuotaLimit,
            gateState);

        var policyResult = CloudTransferOfferPolicy.Create(
            offerId, new CloudReservationId(offerId.Value), new DateTimeOffset(nowUtc, TimeSpan.Zero), OfferDuration, request);

        if (!policyResult.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict(policyResult.Reason!);
        }

        var offerRecord = CloudTransferOfferRecord.Open(
            offerId.Value, shardId, senderOwnerId, recipientOwnerId!.Value, idempotencyKey, nowUtc, policyResult.Offer!.ExpiresAtUtc.UtcDateTime);
        _context.Set<CloudTransferOfferRecord>().Add(offerRecord);

        var targetRows = new List<CloudTransferOfferTargetRecord>(orderedPolicyTargets.Count);
        foreach (var policyTarget in orderedPolicyTargets)
        {
            targetRows.Add(policyTarget.Kind == CloudReservationTargetKind.Item
                ? CloudTransferOfferTargetRecord.ForItem(offerRecord.Id, policyTarget.ItemId!.Value)
                : CloudTransferOfferTargetRecord.ForStackLot(offerRecord.Id, policyTarget.StackLotId!.Value, lockedLotsByLotId[policyTarget.StackLotId!.Value].Quantity));
        }
        _context.Set<CloudTransferOfferTargetRecord>().AddRange(targetRows);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsDuplicateKey(ex))
        {
            await transaction.RollbackAsync(cancellationToken);

            var winner = await _context.Set<CloudTransferOfferRecord>().AsNoTracking()
                .SingleOrDefaultAsync(o => o.CreateIdempotencyKey == idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return CloudBoundaryOutcome<CloudTransferOfferRecord>.Committed(winner);
            }

            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict("This Transfer Offer's idempotency key was already used.");
        }

        var correlationId = Guid.NewGuid();
        foreach (var targetRow in targetRows)
        {
            var biotaId = targetRow.Kind == CloudReservationTargetKind.Item
                ? targetRow.ItemBiotaId!.Value
                : backingBiotaIdByLotId[targetRow.StackLotId!.Value];

            _context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
                correlationId, shardId, CloudBoundaryOperationType.TransferOfferCreated, biotaId, senderOwnerId, CloudBoundaryOutcomeKind.Committed));
        }

        await AddDirectNotificationAsync(shardId, recipientOwnerId.Value, CloudNotificationKind.TransferOfferReceived, "/transfers/offers", correlationId, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudTransferOfferRecord>.Committed(offerRecord);
    }

    /// <summary>The recipient accepts (XFER-002): atomically transfers every offered target to them, or none of them.</summary>
    public Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> AcceptAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(
            () => TryResolveOnceAsync(offerId, actingAccountId, expectedVersion, CloudTransferOfferStatus.Accepted, cancellationToken),
            cancellationToken: cancellationToken);

    /// <summary>The recipient declines (XFER-002): releases every offered target back to the sender.</summary>
    public Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> DeclineAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(
            () => TryResolveOnceAsync(offerId, actingAccountId, expectedVersion, CloudTransferOfferStatus.Declined, cancellationToken),
            cancellationToken: cancellationToken);

    /// <summary>The sender cancels before acceptance (XFER-002): releases every offered target back to the sender.</summary>
    public Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> CancelAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(
            () => TryResolveOnceAsync(offerId, actingAccountId, expectedVersion, CloudTransferOfferStatus.Cancelled, cancellationToken),
            cancellationToken: cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> TryResolveOnceAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CloudTransferOfferStatus targetStatus, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var offer = await LockOfferAsync(offerId, cancellationToken);
        if (offer is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Transfer Offer {offerId} does not exist.");
        }

        var effectiveActingAccountId = await _ownershipResolver.ResolveEffectiveOwnerAccountIdAsync(offer.ShardId, actingAccountId, cancellationToken);
        var actingOwnerId = CloudOwnerIdentity.ForAccount(offer.ShardId, effectiveActingAccountId);

        var requiredActorOwnerId = targetStatus == CloudTransferOfferStatus.Cancelled ? offer.SenderAccountId : offer.RecipientAccountId;
        if (actingOwnerId != requiredActorOwnerId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict(
                $"Transfer Offer {offerId} cannot be resolved by an account that is neither its sender nor its recipient.");
        }

        if (offer.Status != CloudTransferOfferStatus.Pending)
        {
            await transaction.RollbackAsync(cancellationToken);

            if (offer.Status == targetStatus)
            {
                // Idempotent replay (no stored idempotency key needed): repeating an identical
                // already-applied terminal command is a no-op success, mirroring
                // CloudCustodyBoundary.TryCancelWithdrawalReservationOnceAsync's own established
                // "already cancelled" no-op shape.
                return CloudBoundaryOutcome<CloudTransferOfferRecord>.Committed(offer);
            }

            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict(
                $"Transfer Offer {offerId} is already {offer.Status} and cannot be resolved again.");
        }

        if (offer.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict(
                $"Transfer Offer {offerId} is at version {offer.Version}, not the expected version {expectedVersion}.");
        }

        var gateState = await CloudMutationGateReader.ResolveAsync(_context, offer.ShardId, cancellationToken);
        if (gateState == CloudMutationGateState.Frozen)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict(
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);

        if (targetStatus == CloudTransferOfferStatus.Accepted && offer.IsExpiredAt(nowUtc))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Transfer Offer {offerId} expired at {offer.ExpiresAtUtc:O} and can no longer be accepted.");
        }

        var fetchedTargets = await _context.Set<CloudTransferOfferTargetRecord>().AsNoTracking()
            .Where(t => t.OfferId == offerId)
            .ToListAsync(cancellationToken);

        // Deterministic multi-target lock order (transaction rule 2), the exact same
        // targetsByPolicyTarget/orderedPolicyTargets shape TryCreateOnceAsync and
        // TryRedeemWithdrawalReservationOnceAsync already use: a plain DB-return order here would let
        // this resolution acquire CloudCustodyRecord/CloudStackLot locks in a different relative order
        // than a concurrent overlapping CreateAsync/ReserveForWithdrawalAsync call computes
        // independently for the same targets, which is a genuine two-transaction deadlock, not merely
        // a theoretical one.
        var targetsByPolicyTarget = fetchedTargets.ToDictionary(t => t.ToPolicyTarget());
        var targets = CloudReservationTargetOrdering.Order(targetsByPolicyTarget.Keys)
            .Select(policyTarget => targetsByPolicyTarget[policyTarget])
            .ToList();

        offer.Resolve(targetStatus, nowUtc);
        _context.Set<CloudTransferOfferRecord>().Update(offer);

        var correlationId = Guid.NewGuid();

        if (targetStatus == CloudTransferOfferStatus.Accepted)
        {
            // XFER-002: "transfers all offered items or none" -- every target changes owner in this
            // same transaction as the offer's own resolution, using the established OwnershipTransfer
            // ledger/outbox shape so the recipient's inventory read-model and Notification Center
            // (CloudNotificationClassifier) pick it up exactly like any other ownership change.
            foreach (var target in targets)
            {
                uint biotaId;
                if (target.Kind == CloudReservationTargetKind.Item)
                {
                    var record = await LockCustodyRecordByBiotaIdAsync(target.ItemBiotaId!.Value, cancellationToken);
                    if (record is null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Biota {target.ItemBiotaId} no longer has a Cloud Custody Record.");
                    }

                    record.ChangeOwner(offer.RecipientAccountId);
                    _context.CloudCustodyRecords.Update(record);
                    biotaId = record.BiotaId;
                }
                else
                {
                    var lot = await LockStackLotAsync(target.StackLotId!.Value, cancellationToken);
                    if (lot is null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                        return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Cloud Stack Lot {target.StackLotId} no longer exists.");
                    }

                    var lotRecord = await LockCustodyRecordAsync(lot.CustodyRecordId, cancellationToken);
                    lot.ChangeOwner(offer.RecipientAccountId);
                    _context.CloudStackLots.Update(lot);
                    biotaId = lotRecord!.BiotaId;
                }

                _context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
                    correlationId, offer.ShardId, CloudBoundaryOperationType.OwnershipTransfer, biotaId, offer.RecipientAccountId,
                    CloudBoundaryOutcomeKind.Committed, $"Transfer Offer {offer.Id} accepted."));

                var sequenceNumber = await ReserveNextOutboxSequenceNumberAsync(cancellationToken);
                _context.CloudCustodyOutboxEvents.Add(new CloudCustodyOutboxEvent(
                    correlationId, offer.ShardId, CloudBoundaryOperationType.OwnershipTransfer, biotaId, offer.RecipientAccountId, sequenceNumber));
            }

            await AddDirectNotificationAsync(
                offer.ShardId, offer.SenderAccountId, CloudNotificationKind.TransferOfferAccepted, "/dashboard", correlationId, cancellationToken);
        }
        else
        {
            var operationType = targetStatus switch
            {
                CloudTransferOfferStatus.Declined => CloudBoundaryOperationType.TransferOfferDeclined,
                CloudTransferOfferStatus.Cancelled => CloudBoundaryOperationType.TransferOfferCancelled,
                _ => throw new InvalidOperationException($"Unexpected Transfer Offer resolution status {targetStatus}."),
            };

            foreach (var target in targets)
            {
                var biotaId = target.Kind == CloudReservationTargetKind.Item
                    ? target.ItemBiotaId!.Value
                    : (await LockCustodyRecordAsync((await LockStackLotAsync(target.StackLotId!.Value, cancellationToken))!.CustodyRecordId, cancellationToken))!.BiotaId;

                _context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
                    correlationId, offer.ShardId, operationType, biotaId, offer.SenderAccountId, CloudBoundaryOutcomeKind.Committed));
            }

            var (notifiedOwnerId, kind) = targetStatus == CloudTransferOfferStatus.Declined
                ? (offer.SenderAccountId, CloudNotificationKind.TransferOfferDeclined)
                : (offer.RecipientAccountId, CloudNotificationKind.TransferOfferCancelled);

            await AddDirectNotificationAsync(offer.ShardId, notifiedOwnerId, kind, "/transfers/offers", correlationId, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudTransferOfferRecord>.Committed(offer);
    }

    /// <summary>
    /// The database-time expiry worker's own sweep (XFER-002): every currently Pending offer whose
    /// deadline has passed, oldest first, up to <paramref name="batchSize"/>. Each offer expires in
    /// its own transaction (mirrors <c>CloudNotificationProjectionConsumer</c>'s one-event-per-
    /// transaction discipline), so one offer's failure never blocks the rest of the batch.
    /// </summary>
    public async Task<int> ExpireDueOffersAsync(string shardId, int batchSize = 200, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Expiring Transfer Offers requires a Cloud Shard ID.", nameof(shardId));
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "At least one Transfer Offer must be requested per batch.");
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);

        var dueOfferIds = await _context.Set<CloudTransferOfferRecord>().AsNoTracking()
            .Where(o => o.ShardId == shardId && o.Status == CloudTransferOfferStatus.Pending && o.ExpiresAtUtc <= nowUtc)
            .OrderBy(o => o.ExpiresAtUtc)
            .Take(batchSize)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var expiredCount = 0;
        foreach (var offerId in dueOfferIds)
        {
            var outcome = await CloudBoundaryRetry.ExecuteAsync(
                () => TryExpireOnceAsync(offerId, cancellationToken), cancellationToken: cancellationToken);
            if (outcome.Kind == CloudBoundaryOutcomeKind.Committed)
            {
                expiredCount++;
            }
        }

        return expiredCount;
    }

    private async Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> TryExpireOnceAsync(Guid offerId, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var offer = await LockOfferAsync(offerId, cancellationToken);
        if (offer is null || offer.Status != CloudTransferOfferStatus.Pending)
        {
            await transaction.RollbackAsync(cancellationToken);
            return offer is null
                ? CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Transfer Offer {offerId} does not exist.")
                : CloudBoundaryOutcome<CloudTransferOfferRecord>.Committed(offer);
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        if (!offer.IsExpiredAt(nowUtc))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Transfer Offer {offerId} has not yet reached its expiry.");
        }

        var fetchedTargets = await _context.Set<CloudTransferOfferTargetRecord>().AsNoTracking()
            .Where(t => t.OfferId == offerId)
            .ToListAsync(cancellationToken);

        // Deterministic multi-target lock order (transaction rule 2); see TryResolveOnceAsync's
        // matching comment.
        var targetsByPolicyTarget = fetchedTargets.ToDictionary(t => t.ToPolicyTarget());
        var targets = CloudReservationTargetOrdering.Order(targetsByPolicyTarget.Keys)
            .Select(policyTarget => targetsByPolicyTarget[policyTarget])
            .ToList();

        offer.Resolve(CloudTransferOfferStatus.Expired, nowUtc);
        _context.Set<CloudTransferOfferRecord>().Update(offer);

        var correlationId = Guid.NewGuid();
        foreach (var target in targets)
        {
            var biotaId = target.Kind == CloudReservationTargetKind.Item
                ? target.ItemBiotaId!.Value
                : (await LockCustodyRecordAsync((await LockStackLotAsync(target.StackLotId!.Value, cancellationToken))!.CustodyRecordId, cancellationToken))!.BiotaId;

            _context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
                correlationId, offer.ShardId, CloudBoundaryOperationType.TransferOfferExpired, biotaId, offer.SenderAccountId, CloudBoundaryOutcomeKind.Committed));
        }

        await AddDirectNotificationAsync(
            offer.ShardId, offer.SenderAccountId, CloudNotificationKind.TransferOfferExpired, "/transfers/offers", correlationId, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudTransferOfferRecord>.Committed(offer);
    }

    /// <summary>
    /// ADM-004's bulk expiry shift on Global Cloud Maintenance exit is applied directly by
    /// <see cref="CloudGlobalMaintenanceBoundary.ExitAsync"/> (matching its own established inline-SQL
    /// shape for <c>CloudWithdrawalReservation</c>) rather than through this gateway.
    /// </summary>
    public async Task<IReadOnlyList<CloudTransferOfferTargetRecord>> GetTargetsAsync(Guid offerId, CancellationToken cancellationToken = default) =>
        await _context.Set<CloudTransferOfferTargetRecord>().AsNoTracking()
            .Where(t => t.OfferId == offerId)
            .ToListAsync(cancellationToken);

    private static CloudReservationTarget ToPolicyTarget(CloudTransferOfferRequestTarget target) => target.Kind switch
    {
        CloudWithdrawalReservationTargetKind.Item => CloudReservationTarget.ForItem(new CloudItemId(target.ItemBiotaId)),
        CloudWithdrawalReservationTargetKind.StackLot => CloudReservationTarget.ForStackLot(new CloudStackLotId(target.StackLotId)),
        _ => throw new InvalidOperationException("Unrecognized Cloud Transfer Offer request target kind."),
    };

    /// <summary>
    /// Every currently active allocation (any reservation kind) already claiming one of the requested
    /// targets: an active Withdrawal Reservation, or another Pending Transfer Offer. A future Listing/
    /// BidEscrow table joins this same union once it exists, matching
    /// <see cref="CloudGlobalMaintenanceBoundary.ExitAsync"/>'s own forward-looking precedent.
    /// </summary>
    private async Task<Dictionary<CloudReservationTarget, CloudReservationAllocation>> BuildExistingAllocationsAsync(
        IReadOnlyList<uint> requestedBiotaIds, IReadOnlyList<Guid> requestedLotIds, CancellationToken cancellationToken)
    {
        var existingAllocationsByTarget = new Dictionary<CloudReservationTarget, CloudReservationAllocation>();

        var withdrawalConflicts = await (
            from t in _context.CloudWithdrawalReservationTargets.AsNoTracking()
            join r in _context.CloudWithdrawalReservations.AsNoTracking() on t.ReservationId equals r.Id
            where r.Status == CloudReservationStatus.Active
                && ((t.Kind == CloudWithdrawalReservationTargetKind.Item && requestedBiotaIds.Contains(t.ItemBiotaId!.Value))
                    || (t.Kind == CloudWithdrawalReservationTargetKind.StackLot && requestedLotIds.Contains(t.StackLotId!.Value)))
            select new { t.Kind, t.ItemBiotaId, t.StackLotId, r.Id })
            .ToListAsync(cancellationToken);

        foreach (var conflict in withdrawalConflicts)
        {
            var conflictTarget = conflict.Kind == CloudWithdrawalReservationTargetKind.Item
                ? CloudReservationTarget.ForItem(new CloudItemId(conflict.ItemBiotaId!.Value))
                : CloudReservationTarget.ForStackLot(new CloudStackLotId(conflict.StackLotId!.Value));

            existingAllocationsByTarget[conflictTarget] = new CloudReservationAllocation(
                new CloudReservationId(conflict.Id), conflictTarget, CloudReservationKind.Withdrawal, CloudReservationStatus.Active);
        }

        var offerConflicts = await (
            from t in _context.Set<CloudTransferOfferTargetRecord>().AsNoTracking()
            join o in _context.Set<CloudTransferOfferRecord>().AsNoTracking() on t.OfferId equals o.Id
            where o.Status == CloudTransferOfferStatus.Pending
                && ((t.Kind == CloudReservationTargetKind.Item && requestedBiotaIds.Contains(t.ItemBiotaId!.Value))
                    || (t.Kind == CloudReservationTargetKind.StackLot && requestedLotIds.Contains(t.StackLotId!.Value)))
            select new { t.Kind, t.ItemBiotaId, t.StackLotId, o.Id })
            .ToListAsync(cancellationToken);

        foreach (var conflict in offerConflicts)
        {
            var conflictTarget = conflict.Kind == CloudReservationTargetKind.Item
                ? CloudReservationTarget.ForItem(new CloudItemId(conflict.ItemBiotaId!.Value))
                : CloudReservationTarget.ForStackLot(new CloudStackLotId(conflict.StackLotId!.Value));

            existingAllocationsByTarget[conflictTarget] = new CloudReservationAllocation(
                new CloudReservationId(conflict.Id), conflictTarget, CloudReservationKind.Offer, CloudReservationStatus.Active);
        }

        return existingAllocationsByTarget;
    }

    /// <summary>
    /// Resolves a typed current character name to its owning account (XFER-001), querying
    /// ace_shard.character directly on this same connection -- the same cross-schema reach
    /// <see cref="CloudAllegianceVaultGateway.CharacterExistsAndIsNotDeletedAsync"/> already
    /// established, since no other layer yet owns this lookup (<see cref="CloudDisplayCharacterGateway"/>'s
    /// own doc comment: "this class has no ACE-side character read of its own" -- deliberately, because
    /// its caller already gathers a full roster; a single recipient-name lookup has no equivalent
    /// caller-side roster to draw from yet). Not a name-uniqueness assumption beyond what ACE itself
    /// already enforces among currently live characters.
    /// </summary>
    private async Task<(bool Found, uint AccountId)> TryResolveCurrentCharacterAccountAsync(string characterName, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

        // This runs before TryCreateOnceAsync's own transaction begins (transaction rule 9's
        // revalidation happens later, once every target is locked), so nothing else guarantees the
        // underlying connection is still open here -- mirrors
        // CloudAllegianceVaultGateway.CharacterExistsAndIsNotDeletedAsync's own established
        // OpenConnectionAsync/CloseConnectionAsync bracket for the exact same cross-schema reach.
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "SELECT account_Id FROM ace_shard.character WHERE name = @name AND is_Deleted = 0 LIMIT 1;";
            CloudRawSqlHelpers.AddParameter(command, "@name", characterName);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is null or DBNull ? (false, 0) : (true, Convert.ToUInt32(result));
        }
        catch (MySqlConnector.MySqlException ex) when (CloudRawSqlHelpers.IsAccessDenied(ex))
        {
            throw new CloudDatabasePrivilegeException();
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private async Task AddDirectNotificationAsync(
        string shardId, Guid ownerId, CloudNotificationKind kind, string destination, Guid sourceEventId, CancellationToken cancellationToken)
    {
        var sequenceNumber = await CloudLiveStreamSequenceReserver.ReserveNextAsync(_context, cancellationToken);

        var existing = await _context.CloudNotifications
            .Where(n => n.ShardId == shardId && n.OwnerId == ownerId && n.Kind == kind)
            .OrderByDescending(n => n.LatestSourceSequenceNumber)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null && CloudNotificationCoalescingPolicy.ShouldCoalesce(existing.Kind, existing.IsRead, kind))
        {
            existing.RecordOccurrence(sourceEventId, sequenceNumber);
        }
        else
        {
            _context.CloudNotifications.Add(CloudNotification.CreateFirst(shardId, ownerId, kind, destination, sourceEventId, sequenceNumber));
        }

        _context.CloudLiveStreamEvents.Add(new CloudLiveStreamEvent(shardId, sequenceNumber, isPublic: false, ownerId, "Notification", sourceEventId, sequenceNumber));
    }

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

    private async Task LockOwnerQuotaRowAsync(string shardId, Guid ownerId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = """
                INSERT INTO CloudStorageQuotaOwnerLock (OwnerId, ShardId)
                VALUES (@ownerId, @shardId)
                ON DUPLICATE KEY UPDATE OwnerId = OwnerId;
                """;
            CloudRawSqlHelpers.AddParameter(upsert, "@ownerId", ownerId.ToString());
            CloudRawSqlHelpers.AddParameter(upsert, "@shardId", shardId);
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var @lock = connection.CreateCommand();
        @lock.Transaction = transaction;
        @lock.CommandText = "SELECT 1 FROM CloudStorageQuotaOwnerLock WHERE OwnerId = @ownerId AND ShardId = @shardId FOR UPDATE;";
        CloudRawSqlHelpers.AddParameter(@lock, "@ownerId", ownerId.ToString());
        CloudRawSqlHelpers.AddParameter(@lock, "@shardId", shardId);
        await @lock.ExecuteScalarAsync(cancellationToken);
    }

    private async Task<CloudTransferOfferRecord?> LockOfferAsync(Guid offerId, CancellationToken cancellationToken) =>
        await _context.Set<CloudTransferOfferRecord>()
            .FromSqlInterpolated($"SELECT * FROM CloudTransferOffer WHERE Id = {offerId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudCustodyRecord?> LockCustodyRecordByBiotaIdAsync(uint biotaId, CancellationToken cancellationToken) =>
        await _context.CloudCustodyRecords
            .FromSqlInterpolated($"SELECT * FROM CloudCustodyRecord WHERE BiotaId = {biotaId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudCustodyRecord?> LockCustodyRecordAsync(Guid custodyRecordId, CancellationToken cancellationToken) =>
        await _context.CloudCustodyRecords
            .FromSqlInterpolated($"SELECT * FROM CloudCustodyRecord WHERE Id = {custodyRecordId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudStackLot?> LockStackLotAsync(Guid lotId, CancellationToken cancellationToken) =>
        await _context.CloudStackLots
            .FromSqlInterpolated($"SELECT * FROM CloudStackLot WHERE Id = {lotId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<DateTime> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

        // Every other call site runs this after BeginTransactionAsync has already opened the
        // connection, but ExpireDueOffersAsync's own top-level call happens before any transaction --
        // EF Core's OpenConnectionAsync/CloseConnectionAsync are reference-counted, so bracketing here
        // is a harmless no-op for the already-open callers and a real fix for that one.
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "SELECT UTC_TIMESTAMP(6);";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return DateTime.SpecifyKind(Convert.ToDateTime(result), DateTimeKind.Utc);
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }
}
