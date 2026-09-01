using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's own transaction boundary for Allegiance Vault contribute/take
/// (issue #37: VAULT-001, VAULT-002, VAULT-003, WDR-007, INV-004..006): an immediate, Acting-Character-
/// authorized "immediate cloud transfer" (<see cref="CloudOwnershipTransferPolicy"/>'s own doc comment:
/// "for example an Allegiance Vault contribution/take") between a Main Account's personal Cloud
/// Inventory and the vault of the allegiance one of its characters is currently, live, a member of.
///
/// Deliberately a separate class from <see cref="CloudAllegianceVaultGateway"/> (VAULT-004/VAULT-005's
/// existing emptiness-check/Absorption/monarch-deletion gateway) rather than an addition to it: that
/// class's constructor takes no <see cref="ICloudAccountOwnershipResolver"/> and is already exercised
/// by call sites across ACE's own character/allegiance seams
/// (<c>CloudIdentityProjectionConsumerWorker</c>) and its own established test suite with the
/// single-argument constructor; widening that constructor's shape for a capability those callers never
/// need would be an unrelated breaking change. This class instead mirrors
/// <see cref="CloudTransferOfferGateway"/> and <see cref="CloudSharingGrantGateway"/>'s own established
/// shape exactly: an injected <see cref="ICloudAccountOwnershipResolver"/> resolves the caller's
/// personal-inventory side to its effective Main Account, while the vault side is always the resolved
/// <see cref="CloudOwnerIdentity.ForAllegianceVault"/> identity for the Acting Character's own live
/// current monarch (never a caller-supplied vault identity) -- see
/// <see cref="ResolveActingCharacterAsync"/>'s own doc comment for why that determination cannot
/// simply reuse the versioned identity/allegiance cache (CONTEXT.md: "every sensitive action
/// revalidates the current Acting Character").
///
/// WDR-007 ("Allegiance Vault items cannot be withdrawn") and every other prohibited vault action
/// (listing, bidding, Buy It Now, external Transfer Offer) are enforced by construction, not by an
/// explicit check here: every one of those workflows resolves its own acting owner identity from a
/// real authenticated ACE account ID through <see cref="CloudOwnerIdentity.ForAccount"/>, which can
/// never collide with a <see cref="CloudOwnerIdentity.ForAllegianceVault"/> identity (distinct
/// deterministic hash namespaces) -- there is no reachable code path that could name a vault as an
/// ordinary account owner for those workflows. <c>CloudAllegianceVaultBoundaryTests</c> and
/// <c>CloudAllegianceVaultTransactionGatewayTests</c> assert this directly rather than merely relying
/// on it never having been wired up.
/// </summary>
public sealed class CloudAllegianceVaultTransactionGateway : ICloudAllegianceVaultTransactionService
{
    private const short MonarchPropertyType = 26; // PropertyInstanceId.Monarch

    private readonly CloudDbContext _context;
    private readonly ICloudAccountOwnershipResolver _ownershipResolver;

    public CloudAllegianceVaultTransactionGateway(CloudDbContext context, ICloudAccountOwnershipResolver ownershipResolver)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _ownershipResolver = ownershipResolver ?? throw new ArgumentNullException(nameof(ownershipResolver));
    }

    /// <summary>
    /// Contributes <paramref name="target"/> from <paramref name="actingCharacterId"/>'s own effective
    /// personal Cloud Inventory into the Allegiance Vault of the allegiance that character is
    /// currently, live, a member of (VAULT-003: "Immediate personal-to-Allegiance Vault contributions
    /// do not use Transfer Offers"). Repeating this call with the same <paramref name="idempotencyKey"/>
    /// replays the original committed result instead of contributing twice.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>> ContributeAsync(
        string shardId,
        uint callerAccountId,
        uint actingCharacterId,
        CloudReservationTarget target,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        TransferAsync(shardId, callerAccountId, actingCharacterId, target, idempotencyKey, isContribute: true, cancellationToken);

    /// <summary>
    /// Takes <paramref name="target"/> from the Allegiance Vault of the allegiance
    /// <paramref name="actingCharacterId"/> is currently, live, a member of into that character's own
    /// effective personal Cloud Inventory (VAULT-003). Repeating this call with the same
    /// <paramref name="idempotencyKey"/> replays the original committed result instead of taking twice.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>> TakeAsync(
        string shardId,
        uint callerAccountId,
        uint actingCharacterId,
        CloudReservationTarget target,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        TransferAsync(shardId, callerAccountId, actingCharacterId, target, idempotencyKey, isContribute: false, cancellationToken);

    private Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>> TransferAsync(
        string shardId,
        uint callerAccountId,
        uint actingCharacterId,
        CloudReservationTarget target,
        Guid idempotencyKey,
        bool isContribute,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An Allegiance Vault action requires a Cloud Shard ID.", nameof(shardId));
        }

        if (callerAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(callerAccountId), "An Allegiance Vault action requires a real caller account ID.");
        }

        if (actingCharacterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(actingCharacterId), "An Allegiance Vault action requires a real Acting Character GUID.");
        }

        ArgumentNullException.ThrowIfNull(target);

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("An Allegiance Vault action requires a non-empty idempotency key.", nameof(idempotencyKey));
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TryTransferOnceAsync(shardId, callerAccountId, actingCharacterId, target, idempotencyKey, isContribute, cancellationToken),
            cancellationToken: cancellationToken);
    }

    private async Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>> TryTransferOnceAsync(
        string shardId,
        uint callerAccountId,
        uint actingCharacterId,
        CloudReservationTarget target,
        Guid idempotencyKey,
        bool isContribute,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var existingByKey = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existingByKey is not null)
        {
            return await ReplayAsync(existingByKey, isContribute, cancellationToken);
        }

        var effectiveCallerAccountId = await _ownershipResolver.ResolveEffectiveOwnerAccountIdAsync(shardId, callerAccountId, cancellationToken);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(shardId, effectiveCallerAccountId);

        var actingCharacter = await ResolveActingCharacterAsync(actingCharacterId, cancellationToken);
        if (!actingCharacter.Found)
        {
            return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                $"No current character matching Acting Character {actingCharacterId} could be resolved.");
        }

        var effectiveActingAccountId = await _ownershipResolver.ResolveEffectiveOwnerAccountIdAsync(shardId, actingCharacter.AccountId, cancellationToken);
        if (effectiveActingAccountId != effectiveCallerAccountId)
        {
            return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                $"Acting Character {actingCharacterId} does not currently belong to the caller's own Main/Linked account group.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // ARCH-006 / transaction rule 4: a concurrent identical request may have committed between
        // the unlocked check above and this transaction's start; re-check now that a serialized writer
        // would have already committed (mirrors CloudOwnershipTransferAuthority.TryTransferOnceAsync's
        // own established double-check rationale).
        var existingByKeyAfterOpen = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
        if (existingByKeyAfterOpen is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReplayAsync(existingByKeyAfterOpen, isContribute, cancellationToken);
        }

        CloudCustodyRecord? record;
        CloudStackLot? lot = null;
        uint biotaId;

        if (target.Kind == CloudReservationTargetKind.Item)
        {
            record = await LockCustodyRecordByBiotaIdAsync(target.ItemId!.Value, cancellationToken);
            if (record is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict($"Biota {target.ItemId} has no Cloud Custody Record.");
            }

            if (record.IsStack)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                    $"Biota {target.ItemId} is a stack Cloud Custody Record; act on its Cloud Stack Lot(s) instead.");
            }

            biotaId = record.BiotaId;
        }
        else
        {
            lot = await LockStackLotAsync(target.StackLotId!.Value, cancellationToken);
            if (lot is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict($"Cloud Stack Lot {target.StackLotId} does not exist.");
            }

            record = await LockCustodyRecordAsync(lot.CustodyRecordId, cancellationToken);
            if (record is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict($"Cloud Stack Lot {target.StackLotId} does not exist.");
            }

            biotaId = record.BiotaId;
        }

        // Transaction rule 9: revalidate the Acting Character's live membership again now that the
        // target is locked -- an AllegianceBroken between the unlocked check above and this point must
        // not let a stale authorization commit (issue #37's Red requirement: "revalidation at commit").
        var actingCharacterAtCommit = await ResolveActingCharacterAsync(actingCharacterId, cancellationToken);
        if (!actingCharacterAtCommit.Found)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                $"Acting Character {actingCharacterId} no longer exists (or was deleted).");
        }

        var currentOwnerId = target.Kind == CloudReservationTargetKind.Item ? record.OwnerId!.Value : lot!.OwnerId;

        var vaultOwnerId = actingCharacterAtCommit.CurrentMonarchId is { } liveMonarchId
            ? CloudOwnerIdentity.ForAllegianceVault(shardId, liveMonarchId)
            : (Guid?)null;

        if (vaultOwnerId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                $"Acting Character {actingCharacterId} does not currently belong to any allegiance, so it has no Allegiance Vault to act for.");
        }

        var expectedSourceOwnerId = isContribute ? personalOwnerId : vaultOwnerId.Value;
        var destinationOwnerId = isContribute ? vaultOwnerId.Value : personalOwnerId;

        if (currentOwnerId != expectedSourceOwnerId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                isContribute
                    ? $"{target} is not owned by Acting Character {actingCharacterId}'s own personal Cloud Inventory."
                    : $"{target} is not currently held by Acting Character {actingCharacterId}'s Allegiance Vault.");
        }

        var activeAllocation = await FindActiveReservationAllocationAsync(target, cancellationToken);
        var gateState = await CloudMutationGateReader.ResolveAsync(_context, shardId, cancellationToken);

        await LockOwnerQuotaRowAsync(shardId, destinationOwnerId, cancellationToken);
        var destinationQuotaLimit = isContribute
            ? await CloudStorageQuotaReader.GetVaultLimitAsync(_context, shardId, cancellationToken)
            : await CloudStorageQuotaReader.GetPersonalLimitAsync(_context, shardId, cancellationToken);
        var destinationCurrentProjectedCount = await CloudStackQuotaProjection.CountProjectedItemsAsync(_context, shardId, destinationOwnerId, cancellationToken);

        var authorization = CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter(new CloudAllegianceVaultActionRequest(
            actingCharacterAtCommit.Found, actingCharacterAtCommit.CurrentMonarchId, destinationCurrentProjectedCount, destinationQuotaLimit, gateState));

        if (!authorization.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(authorization.Reason!);
        }

        var transferPolicyResult = CloudOwnershipTransferPolicy.Transfer(
            target,
            new CloudAccountId(currentOwnerId),
            new CloudAccountId(destinationOwnerId),
            new CloudAggregateVersion(record.Version),
            new CloudAggregateVersion(record.Version),
            activeAllocation,
            gateState);

        if (!transferPolicyResult.IsSuccess)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(transferPolicyResult.Reason!);
        }

        if (target.Kind == CloudReservationTargetKind.Item)
        {
            record.ChangeOwner(destinationOwnerId);
            _context.CloudCustodyRecords.Update(record);
        }
        else
        {
            lot!.ChangeOwner(destinationOwnerId);
            _context.CloudStackLots.Update(lot);
        }

        await _context.SaveChangesAsync(cancellationToken);

        var operationType = isContribute ? CloudBoundaryOperationType.VaultContribution : CloudBoundaryOperationType.VaultTake;
        var correlationId = Guid.NewGuid();

        _context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
            correlationId, shardId, operationType, biotaId, destinationOwnerId, CloudBoundaryOutcomeKind.Committed,
            $"Acting Character {actingCharacterId} {(isContribute ? "contributed" : "took")} {target}."));

        var sequenceNumber = await ReserveNextOutboxSequenceNumberAsync(cancellationToken);
        _context.CloudCustodyOutboxEvents.Add(new CloudCustodyOutboxEvent(
            correlationId, shardId, operationType, biotaId, destinationOwnerId, sequenceNumber));

        if (!isContribute)
        {
            // A vault has no single logged-in owner to notify on contribution, but a take lands
            // squarely in the Acting Character's own personal Cloud Inventory, matching every other
            // immediate ownership transfer's own OwnershipReceived notification.
            await AddDirectNotificationAsync(shardId, destinationOwnerId, CloudNotificationKind.OwnershipReceived, "/dashboard", correlationId, cancellationToken);
        }

        _context.CloudIdempotencyRecords.Add(new CloudIdempotencyRecord(
            idempotencyKey, shardId, operationType, biotaId, destinationOwnerId, record.Id, targetContainerId: null, correlationId));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsDuplicateKey(ex))
        {
            // A concurrent attempt for this exact idempotency key won the race; replay whichever
            // attempt actually committed instead of reporting an unrelated-looking Conflict
            // (transaction rules 4 and 8), mirroring CloudOwnershipTransferAuthority's own established
            // handling of the identical race.
            await transaction.RollbackAsync(cancellationToken);

            var winner = await FindIdempotencyRecordAsync(idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return await ReplayAsync(winner, isContribute, cancellationToken);
            }

            return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                "A concurrent Allegiance Vault action for this idempotency key already committed.");
        }

        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Committed(
            new CloudAllegianceVaultTransferResult(biotaId, personalOwnerId, vaultOwnerId.Value));
    }

    private async Task<CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>> ReplayAsync(
        CloudIdempotencyRecord existing, bool isContribute, CancellationToken cancellationToken)
    {
        var expectedOperationType = isContribute ? CloudBoundaryOperationType.VaultContribution : CloudBoundaryOperationType.VaultTake;
        if (existing.OperationType != expectedOperationType)
        {
            return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Conflict(
                $"Idempotency key {existing.IdempotencyKey} was already committed as a {existing.OperationType}, not a {expectedOperationType}.");
        }

        var record = await _context.CloudCustodyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == existing.CustodyRecordId, cancellationToken);
        if (record is null)
        {
            throw new CloudCustodyConflictException(
                $"Idempotency key {existing.IdempotencyKey} committed an Allegiance Vault transfer whose Cloud Custody Record no longer exists.");
        }

        return CloudBoundaryOutcome<CloudAllegianceVaultTransferResult>.Committed(
            new CloudAllegianceVaultTransferResult(existing.BiotaId, existing.OwnerId, existing.OwnerId));
    }

    /// <summary>
    /// Resolves <paramref name="characterId"/>'s owning account and current live top-level monarch
    /// directly against ace_shard (VAULT-001), never the versioned identity/allegiance cache: "every
    /// sensitive action revalidates the current Acting Character" (CONTEXT.md), and that cache has a
    /// known gap for exactly this determination -- a character who becomes a monarch purely by
    /// gaining their first vassal (<c>Player_Allegiance.SwearAllegiance</c> only ever publishes an
    /// identity event for the *vassal* side) never receives a projection event recording their own
    /// <c>MonarchId</c>, so the cache alone cannot distinguish a genuine monarch from an unaffiliated
    /// character. The persisted <c>PropertyInstanceId.Monarch</c> (type 26) instance property, when
    /// present, is already the tree's ultimate top-level monarch (<c>AllegianceManager.GetMonarch</c>
    /// is what originally computed it), whether that value is someone else or the character's own GUID
    /// (<c>Player_Allegiance.HandleActionBreakAllegiance</c> explicitly self-assigns it to a freed
    /// vassal who retains their own descendants). When the property is absent, the character is a
    /// live current monarch of their own allegiance only if at least one other live character's own
    /// Monarch property currently points back at them; otherwise they belong to no allegiance at all.
    /// </summary>
    private async Task<CloudActingCharacterMembership> ResolveActingCharacterAsync(uint characterId, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        // EF Core's OpenConnectionAsync/CloseConnectionAsync are reference-counted, so this bracket is
        // a harmless no-op when called from within an already-open transaction and a real open/close
        // when called from the pre-transaction unlocked check, matching
        // CloudTransferOfferGateway.GetDatabaseUtcNowAsync's own established rationale for the same
        // pattern.
        await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            uint accountId;
            await using (var characterCommand = connection.CreateCommand())
            {
                characterCommand.Transaction = transaction;
                characterCommand.CommandText = "SELECT account_Id FROM ace_shard.character WHERE id = @id AND is_Deleted = 0;";
                CloudRawSqlHelpers.AddParameter(characterCommand, "@id", characterId);

                var accountResult = await characterCommand.ExecuteScalarAsync(cancellationToken);
                if (accountResult is null or DBNull)
                {
                    return CloudActingCharacterMembership.NotFound;
                }

                accountId = Convert.ToUInt32(accountResult);
            }

            uint? ownMonarchProperty;
            await using (var monarchCommand = connection.CreateCommand())
            {
                monarchCommand.Transaction = transaction;
                monarchCommand.CommandText = "SELECT value FROM ace_shard.biota_properties_i_i_d WHERE object_Id = @id AND type = @type;";
                CloudRawSqlHelpers.AddParameter(monarchCommand, "@id", characterId);
                CloudRawSqlHelpers.AddParameter(monarchCommand, "@type", MonarchPropertyType);

                var monarchResult = await monarchCommand.ExecuteScalarAsync(cancellationToken);
                ownMonarchProperty = monarchResult is null or DBNull ? null : Convert.ToUInt32(monarchResult);
            }

            if (ownMonarchProperty is { } presentMonarchId)
            {
                return new CloudActingCharacterMembership(Found: true, accountId, presentMonarchId);
            }

            await using (var vassalCommand = connection.CreateCommand())
            {
                vassalCommand.Transaction = transaction;
                vassalCommand.CommandText =
                    "SELECT COUNT(*) FROM ace_shard.biota_properties_i_i_d WHERE type = @type AND value = @id;";
                CloudRawSqlHelpers.AddParameter(vassalCommand, "@type", MonarchPropertyType);
                CloudRawSqlHelpers.AddParameter(vassalCommand, "@id", characterId);

                var vassalCount = Convert.ToInt64(await vassalCommand.ExecuteScalarAsync(cancellationToken));
                return new CloudActingCharacterMembership(Found: true, accountId, vassalCount > 0 ? characterId : null);
            }
        }
        finally
        {
            await _context.Database.CloseConnectionAsync();
        }
    }

    private readonly record struct CloudActingCharacterMembership(bool Found, uint AccountId, uint? CurrentMonarchId)
    {
        public static readonly CloudActingCharacterMembership NotFound = new(false, 0, null);
    }

    /// <summary>
    /// Every currently active allocation (any reservation kind) already claiming <paramref name="target"/>
    /// -- an active Withdrawal Reservation or a Pending Transfer Offer -- matching
    /// <see cref="CloudTransferOfferGateway"/>'s own established union query, generalized to a single
    /// target instead of a batch. A vault-owned target should never legitimately have one (this
    /// codebase's own doc comments: "Vault contents can never carry an active exclusive reservation"),
    /// but this is checked anyway as a defense-in-depth backstop rather than assumed.
    /// </summary>
    private async Task<CloudReservationAllocation?> FindActiveReservationAllocationAsync(CloudReservationTarget target, CancellationToken cancellationToken)
    {
        if (target.Kind == CloudReservationTargetKind.Item)
        {
            var biotaId = target.ItemId!.Value;

            var withdrawalReservationId = await (
                from t in _context.CloudWithdrawalReservationTargets.AsNoTracking()
                join r in _context.CloudWithdrawalReservations.AsNoTracking() on t.ReservationId equals r.Id
                where t.Kind == CloudWithdrawalReservationTargetKind.Item && t.ItemBiotaId == biotaId && r.Status == CloudReservationStatus.Active
                select (Guid?)r.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (withdrawalReservationId is not null)
            {
                return new CloudReservationAllocation(
                    new CloudReservationId(withdrawalReservationId.Value), target, CloudReservationKind.Withdrawal, CloudReservationStatus.Active);
            }

            var offerId = await (
                from t in _context.Set<CloudTransferOfferTargetRecord>().AsNoTracking()
                join o in _context.Set<CloudTransferOfferRecord>().AsNoTracking() on t.OfferId equals o.Id
                where t.Kind == CloudReservationTargetKind.Item && t.ItemBiotaId == biotaId && o.Status == CloudTransferOfferStatus.Pending
                select (Guid?)o.Id)
                .SingleOrDefaultAsync(cancellationToken);

            return offerId is null
                ? null
                : new CloudReservationAllocation(new CloudReservationId(offerId.Value), target, CloudReservationKind.Offer, CloudReservationStatus.Active);
        }
        else
        {
            var lotId = target.StackLotId!.Value;

            var withdrawalReservationId = await (
                from t in _context.CloudWithdrawalReservationTargets.AsNoTracking()
                join r in _context.CloudWithdrawalReservations.AsNoTracking() on t.ReservationId equals r.Id
                where t.Kind == CloudWithdrawalReservationTargetKind.StackLot && t.StackLotId == lotId && r.Status == CloudReservationStatus.Active
                select (Guid?)r.Id)
                .SingleOrDefaultAsync(cancellationToken);

            if (withdrawalReservationId is not null)
            {
                return new CloudReservationAllocation(
                    new CloudReservationId(withdrawalReservationId.Value), target, CloudReservationKind.Withdrawal, CloudReservationStatus.Active);
            }

            var offerId = await (
                from t in _context.Set<CloudTransferOfferTargetRecord>().AsNoTracking()
                join o in _context.Set<CloudTransferOfferRecord>().AsNoTracking() on t.OfferId equals o.Id
                where t.Kind == CloudReservationTargetKind.StackLot && t.StackLotId == lotId && o.Status == CloudTransferOfferStatus.Pending
                select (Guid?)o.Id)
                .SingleOrDefaultAsync(cancellationToken);

            return offerId is null
                ? null
                : new CloudReservationAllocation(new CloudReservationId(offerId.Value), target, CloudReservationKind.Offer, CloudReservationStatus.Active);
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

    private async Task<CloudIdempotencyRecord?> FindIdempotencyRecordAsync(Guid idempotencyKey, CancellationToken cancellationToken) =>
        await _context.CloudIdempotencyRecords.AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

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

    /// <summary>
    /// Locks <see cref="CloudCustodyOutboxSequence"/>'s single row and returns the next durable order
    /// position, the same locking approach every other Custody Outbox append in this project uses
    /// (ARCH-007).
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
}

/// <summary>The committed result of one <see cref="CloudAllegianceVaultTransactionGateway"/> contribute/take.</summary>
public sealed record CloudAllegianceVaultTransferResult(uint BiotaId, Guid PersonalOwnerId, Guid VaultOwnerId);
