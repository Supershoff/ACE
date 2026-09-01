using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's own transaction boundary for personal Sharing Grants (issue #36,
/// SHARE-001..004, AUTH-008, WDR-002): locked, idempotent-by-value set/revoke, plus the AUTH-008
/// account-link revocation path <c>CloudAccountLinkGateway</c>'s own doc comment documents as a known
/// gap ("Once it does, its revocation must be added to LinkAsync's same transaction"). Distinct from
/// <see cref="CloudCustodyBoundary"/>: a Sharing Grant never touches a native biota's Container/
/// Wielder/Location (ARCH-004) -- it only ever changes who may view/create Withdrawal Tokens for an
/// owner's already-off-world personal Cloud Inventory.
///
/// Unlike <see cref="CloudTransferOfferGateway"/>, a Sharing Grant "set" carries no caller-supplied
/// idempotency key: setting a grant to a level is naturally idempotent by value rather than by key
/// (<see cref="CloudSharingGrantRecord.TrySetLevel"/> is a no-op, including skipping the ledger/
/// notification side effects, when the requested level already matches), so a repeated identical
/// request converges to the same committed state without needing a stored key to detect the replay.
/// </summary>
public sealed class CloudSharingGrantGateway
{
    private readonly CloudDbContext _context;
    private readonly ICloudAccountOwnershipResolver _ownershipResolver;

    public CloudSharingGrantGateway(CloudDbContext context, ICloudAccountOwnershipResolver ownershipResolver)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _ownershipResolver = ownershipResolver ?? throw new ArgumentNullException(nameof(ownershipResolver));
    }

    /// <summary>
    /// Sets (creates, changes, or explicitly revokes) the owner's Sharing Grant to the resolved
    /// grantee's ownership group (SHARE-001, SHARE-004). Resolves <paramref name="granteeCharacterName"/>
    /// to its current owning account's effective Main Account exactly once, matching
    /// <see cref="CloudTransferOfferGateway.CreateAsync"/>'s own established XFER-001 shape.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudSharingGrantRecord>> SetAsync(
        string shardId,
        uint ownerAccountId,
        string granteeCharacterName,
        CloudSharingGrantLevel requestedLevel,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Sharing Grant requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerAccountId), "A Sharing Grant requires a real owner account ID.");
        }

        if (string.IsNullOrWhiteSpace(granteeCharacterName))
        {
            throw new ArgumentException("A Sharing Grant requires a typed grantee character name.", nameof(granteeCharacterName));
        }

        return CloudBoundaryRetry.ExecuteAsync(
            () => TrySetOnceAsync(shardId, ownerAccountId, granteeCharacterName, requestedLevel, cancellationToken),
            cancellationToken: cancellationToken);
    }

    private async Task<CloudBoundaryOutcome<CloudSharingGrantRecord>> TrySetOnceAsync(
        string shardId, uint ownerAccountId, string granteeCharacterName, CloudSharingGrantLevel requestedLevel, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var effectiveOwnerAccountId = await _ownershipResolver.ResolveEffectiveOwnerAccountIdAsync(shardId, ownerAccountId, cancellationToken);
        var ownerId = CloudOwnerIdentity.ForAccount(shardId, effectiveOwnerAccountId);

        var (granteeFound, granteeCharacterAccountId) = await TryResolveCurrentCharacterAccountAsync(granteeCharacterName, cancellationToken);
        Guid? granteeId = null;
        if (granteeFound)
        {
            var effectiveGranteeAccountId = await _ownershipResolver.ResolveEffectiveOwnerAccountIdAsync(shardId, granteeCharacterAccountId, cancellationToken);
            granteeId = CloudOwnerIdentity.ForAccount(shardId, effectiveGranteeAccountId);
        }

        var gateState = await CloudMutationGateReader.ResolveAsync(_context, shardId, cancellationToken);

        var request = new CloudSharingGrantSetRequest(
            new CloudAccountId(ownerId), granteeFound, granteeId is null ? null : new CloudAccountId(granteeId.Value),
            granteeIsCrossShard: false, requestedLevel, gateState);

        var policyResult = CloudSharingGrantPolicy.EvaluateSet(request);
        if (!policyResult.IsSuccess)
        {
            return CloudBoundaryOutcome<CloudSharingGrantRecord>.Conflict(policyResult.Reason!);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);
        var existing = await LockGrantAsync(shardId, ownerId, granteeId!.Value, cancellationToken);

        if (existing is null)
        {
            var created = CloudSharingGrantRecord.Open(Guid.NewGuid(), shardId, ownerId, granteeId.Value, requestedLevel, nowUtc);
            _context.Set<CloudSharingGrantRecord>().Add(created);

            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsDuplicateKey(ex))
            {
                // A concurrent first-time Set for the exact same (owner, grantee) pair won the race;
                // roll back and re-run this attempt, which will now find and lock the winner's row
                // through the ordinary update path below.
                await transaction.RollbackAsync(cancellationToken);
                return await TrySetOnceAsync(shardId, ownerAccountId, granteeCharacterName, requestedLevel, cancellationToken);
            }

            await AppendChangeSideEffectsAsync(created, previousLevel: null, correlationId: Guid.NewGuid(), nowUtc, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudSharingGrantRecord>.Committed(created);
        }

        var previousLevel = existing.Level;
        var changed = existing.TrySetLevel(requestedLevel, nowUtc);
        if (changed)
        {
            _context.Set<CloudSharingGrantRecord>().Update(existing);
            await _context.SaveChangesAsync(cancellationToken);
            await AppendChangeSideEffectsAsync(existing, previousLevel, correlationId: Guid.NewGuid(), nowUtc, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return CloudBoundaryOutcome<CloudSharingGrantRecord>.Committed(existing);
    }

    /// <summary>
    /// Writes the ledger event and grantee notification for a real value change, and -- when the
    /// grant no longer resolves to View & Withdraw (it was downgraded or explicitly revoked) --
    /// releases every still-Active grant-derived Withdrawal Reservation this exact grant authorized
    /// (SHARE-004: "invalidates unredeemed Withdrawal Tokens created through it"). A brand-new grant
    /// (<paramref name="previousLevel"/> null) can never have any reservation to invalidate.
    /// </summary>
    private async Task AppendChangeSideEffectsAsync(
        CloudSharingGrantRecord grant, CloudSharingGrantLevel? previousLevel, Guid correlationId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        _context.Set<CloudSharingGrantLedgerEvent>().Add(new CloudSharingGrantLedgerEvent(
            correlationId, grant.ShardId, CloudSharingGrantLedgerEventType.LevelSet, grant.OwnerId, grant.GranteeId,
            reason: $"Sharing Grant set to {grant.Level}."));

        await AddDirectNotificationAsync(
            grant.ShardId, grant.GranteeId, CloudNotificationKind.SharingGrantChanged, "/sharing", correlationId, cancellationToken);

        if (previousLevel == CloudSharingGrantLevel.ViewAndWithdraw && grant.Level != CloudSharingGrantLevel.ViewAndWithdraw)
        {
            await ReleaseGrantDerivedReservationsAsync(grant, correlationId, nowUtc, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Releases every still-Active <see cref="CloudWithdrawalReservation"/> bound to
    /// <paramref name="grant"/>'s ID (SHARE-004). A reservation already released (redeemed, cancelled,
    /// or expired) is left untouched -- there is nothing left to invalidate.
    /// </summary>
    private async Task ReleaseGrantDerivedReservationsAsync(
        CloudSharingGrantRecord grant, Guid correlationId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var affectedReservationIds = await _context.CloudWithdrawalReservations.AsNoTracking()
            .Where(r => r.SharingGrantId == grant.Id && r.Status == CloudReservationStatus.Active)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        foreach (var reservationId in affectedReservationIds)
        {
            var reservation = await LockWithdrawalReservationAsync(reservationId, cancellationToken);
            if (reservation is null || reservation.Status != CloudReservationStatus.Active)
            {
                // Already resolved by a race (redeemed/cancelled/expired) between the unlocked scan
                // above and this row's lock -- nothing left to invalidate.
                continue;
            }

            reservation.Release(nowUtc, CloudReservationReleaseReason.SharingGrantAuthorityLost);
            _context.CloudWithdrawalReservations.Update(reservation);

            var targets = await _context.CloudWithdrawalReservationTargets.AsNoTracking()
                .Where(t => t.ReservationId == reservationId)
                .ToListAsync(cancellationToken);

            foreach (var target in targets)
            {
                var biotaId = await ResolveTargetBackingBiotaIdAsync(target, cancellationToken);
                if (biotaId is null)
                {
                    continue;
                }

                _context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
                    correlationId, grant.ShardId, CloudBoundaryOperationType.WithdrawalReservationInvalidated,
                    biotaId.Value, reservation.OwnerId, CloudBoundaryOutcomeKind.Committed,
                    $"Sharing Grant {grant.Id} no longer authorizes this Withdrawal Token."));
            }

            await AddDirectNotificationAsync(
                grant.ShardId, reservation.RedeemerOwnerId ?? reservation.OwnerId, CloudNotificationKind.SharingGrantWithdrawalLost,
                "/account/withdrawal", correlationId, cancellationToken);
        }
    }

    /// <summary>
    /// Revokes every Sharing Grant naming <paramref name="accountOwnerId"/> as owner or grantee to
    /// None (AUTH-008: "Linking revokes every incoming and outgoing personal Sharing Grant associated
    /// with the source account"). Runs against the caller's own already-open transaction/context
    /// (<c>CloudAccountLinkGateway.LinkOnceAsync</c>'s own transaction) rather than opening a new one,
    /// exactly like that method's other in-transaction side effects -- it must commit or roll back
    /// atomically with the link itself, not as a separate boundary call.
    /// </summary>
    internal async Task RevokeAllForAccountWithinCallersTransactionAsync(
        string shardId, Guid accountOwnerId, Guid correlationId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var affectedGrantIds = await _context.Set<CloudSharingGrantRecord>().AsNoTracking()
            .Where(g => g.ShardId == shardId && (g.OwnerId == accountOwnerId || g.GranteeId == accountOwnerId) && g.Level != CloudSharingGrantLevel.None)
            .Select(g => g.Id)
            .ToListAsync(cancellationToken);

        foreach (var grantId in affectedGrantIds)
        {
            var grant = await LockGrantByIdAsync(grantId, cancellationToken);
            if (grant is null)
            {
                continue;
            }

            var previousLevel = grant.Level;
            if (!grant.TrySetLevel(CloudSharingGrantLevel.None, nowUtc))
            {
                continue;
            }

            _context.Set<CloudSharingGrantRecord>().Update(grant);

            _context.Set<CloudSharingGrantLedgerEvent>().Add(new CloudSharingGrantLedgerEvent(
                correlationId, shardId, CloudSharingGrantLedgerEventType.RevokedByAccountLink, grant.OwnerId, grant.GranteeId,
                reason: "Account linking revoked this Sharing Grant (AUTH-008)."));

            if (previousLevel == CloudSharingGrantLevel.ViewAndWithdraw)
            {
                await ReleaseGrantDerivedReservationsAsync(grant, correlationId, nowUtc, cancellationToken);
            }
        }

        if (affectedGrantIds.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<CloudSharingGrantRecord?> LockGrantAsync(string shardId, Guid ownerId, Guid granteeId, CancellationToken cancellationToken) =>
        await _context.Set<CloudSharingGrantRecord>()
            .FromSqlInterpolated($"SELECT * FROM CloudSharingGrant WHERE ShardId = {shardId} AND OwnerId = {ownerId} AND GranteeId = {granteeId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudSharingGrantRecord?> LockGrantByIdAsync(Guid grantId, CancellationToken cancellationToken) =>
        await _context.Set<CloudSharingGrantRecord>()
            .FromSqlInterpolated($"SELECT * FROM CloudSharingGrant WHERE Id = {grantId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudWithdrawalReservation?> LockWithdrawalReservationAsync(Guid reservationId, CancellationToken cancellationToken) =>
        await _context.CloudWithdrawalReservations
            .FromSqlInterpolated($"SELECT * FROM CloudWithdrawalReservation WHERE Id = {reservationId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<uint?> ResolveTargetBackingBiotaIdAsync(CloudWithdrawalReservationTarget target, CancellationToken cancellationToken)
    {
        if (target.Kind == CloudWithdrawalReservationTargetKind.Item)
        {
            return target.ItemBiotaId;
        }

        var lot = await _context.CloudStackLots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == target.StackLotId!.Value, cancellationToken);
        if (lot is null)
        {
            return null;
        }

        var record = await _context.CloudCustodyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Id == lot.CustodyRecordId, cancellationToken);
        return record?.BiotaId;
    }

    /// <summary>
    /// Resolves a typed current character name to its owning account (SHARE-001), matching
    /// <see cref="CloudTransferOfferGateway"/>'s own identically-named, identically-shaped private
    /// helper exactly (no other layer yet owns this lookup for a single-name resolution -- see that
    /// method's own doc comment).
    /// </summary>
    private async Task<(bool Found, uint AccountId)> TryResolveCurrentCharacterAccountAsync(string characterName, CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

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

    private async Task<DateTime> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();

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
