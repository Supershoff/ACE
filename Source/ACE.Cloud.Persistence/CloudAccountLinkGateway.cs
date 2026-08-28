using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's own transaction boundary for account linking (AUTH-005..009):
/// locked, idempotent link/unlink against immutable ACE account IDs, and the read-side resolution
/// every Cloud Custodian deposit uses to route a linked account's future deposits to its Main
/// Account (AUTH-005). Distinct from <see cref="CloudCustodyBoundary"/> (ACE's World Boundary
/// Authority gateway): account linking creates or ends a group membership and reassigns existing
/// Cloud ownership, but it never touches a native biota's Container/Wielder/Location (ARCH-004).
///
/// Global Cloud Maintenance and Marketplace State are full administrative aggregates out of scope
/// for this issue (see <see cref="CloudMutationGateState"/>'s own doc comment); this gateway
/// therefore always evaluates against <see cref="CloudMutationGateState.Open"/>, matching every
/// other Cloud Transaction Authority call site established so far (for example
/// <see cref="CloudCustodyBoundary.ReserveForWithdrawalAsync"/>).
///
/// Known gap, documented rather than silently omitted: AUTH-008's "Linking revokes every personal
/// Sharing Grant associated with the source account" cannot be implemented yet because no
/// SharingGrant table exists in this schema (SHARE-001..004 land in a later issue). Once it does,
/// its revocation must be added to <see cref="LinkAsync"/>'s same transaction. Likewise, AUTH-009's
/// active-auction self-dealing check and the "pending obligations" check below only inspect
/// obligation types that already exist in this schema (Withdrawal Tokens); listings, bids,
/// settlements, and Transfer Offers (MKT-*, XFER-*) do not exist yet and so can never block a link
/// today -- <paramref name="wouldCreateActiveAuctionConflict"/> on <see cref="LinkAsync"/> exists so
/// a future marketplace-aware caller can wire in that check without changing this method's shape.
/// </summary>
public sealed class CloudAccountLinkGateway
{
    private readonly CloudDbContext _context;

    public CloudAccountLinkGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Links <paramref name="sourceAccountId"/> into <paramref name="mainAccountId"/>'s ownership
    /// group (AUTH-005): approves or refuses per <see cref="CloudAccountLinkPolicy.EvaluateLink"/>
    /// under a locked, commit-time revalidation, and -- only on approval -- transfers every existing
    /// Cloud asset the source account owns to the Main Account's owner identity, atomically (all
    /// eligible assets or none, since the whole operation is one database transaction). Repeating
    /// this call with the same <paramref name="idempotencyKey"/> replays the original committed
    /// result, approved or rejected, instead of re-deciding eligibility against since-changed state
    /// (transaction rules 4 and 8).
    /// </summary>
    public async Task<CloudAccountLinkOutcome> LinkAsync(
        string shardId,
        uint mainAccountId,
        uint sourceAccountId,
        Guid idempotencyKey,
        bool wouldCreateActiveAuctionConflict = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Linking an account requires a Cloud Shard ID.", nameof(shardId));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("Linking an account requires an idempotency key.", nameof(idempotencyKey));
        }

        _context.ChangeTracker.Clear();

        var existing = await _context.Set<CloudAccountLinkIdempotencyRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return ReplayOutcome(existing);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Deterministic lock ordering (transaction rule 2): always lock the lower account ID's
        // marker row first, so a concurrent link/unlink touching the same two accounts in the
        // opposite role never deadlocks against this one.
        var (firstId, secondId) = mainAccountId < sourceAccountId ? (mainAccountId, sourceAccountId) : (sourceAccountId, mainAccountId);
        var firstMarker = await LockActiveLinkMarkerAsync(shardId, firstId, cancellationToken);
        var secondMarker = await LockActiveLinkMarkerAsync(shardId, secondId, cancellationToken);

        var mainMarker = mainAccountId == firstId ? firstMarker : secondMarker;
        var sourceMarker = sourceAccountId == firstId ? firstMarker : secondMarker;

        var sourceHasLinkedAccounts = await SourceHasActiveChildrenAsync(shardId, sourceAccountId, cancellationToken);
        var sourceHasPendingObligations = await SourceHasPendingObligationsAsync(shardId, sourceAccountId, cancellationToken);

        var request = new CloudAccountLinkRequest(
            mainAccountId,
            sourceAccountId,
            mainAccountIsLinkedElsewhere: mainMarker is not null,
            sourceIsAlreadyLinked: sourceMarker is not null,
            sourceHasLinkedAccounts,
            sourceHasPendingObligations,
            wouldCreateActiveAuctionConflict,
            CloudMutationGateState.Open);

        var decision = CloudAccountLinkPolicy.EvaluateLink(request);
        var correlationId = Guid.NewGuid();

        if (!decision.IsApproved)
        {
            _context.Add(new CloudAccountLinkLedgerEvent(
                correlationId, shardId, CloudAccountLinkLedgerEventType.LinkRejected, mainAccountId, sourceAccountId, decision.RejectionCode.ToString()));
            _context.Add(new CloudAccountLinkIdempotencyRecord(
                idempotencyKey, shardId, CloudAccountLinkOperationType.Link, mainAccountId, sourceAccountId,
                isApproved: false, decision.RejectionCode, accountLinkId: null, ownershipGroupId: null, correlationId));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CloudAccountLinkOutcome.Rejected(decision.RejectionCode);
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);

        var group = await _context.Set<CloudOwnershipGroup>()
            .SingleOrDefaultAsync(g => g.ShardId == shardId && g.MainAccountId == mainAccountId, cancellationToken);
        if (group is null)
        {
            group = new CloudOwnershipGroup(shardId, mainAccountId);
            _context.Add(group);
        }

        var link = CloudAccountLink.Open(group.Id, shardId, sourceAccountId, nowUtc);
        _context.Add(link);
        _context.Add(new CloudActiveAccountLinkMarker(shardId, sourceAccountId, link.Id, group.Id));

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (CloudRawSqlHelpers.IsDuplicateKey(ex))
        {
            // A concurrent link for this exact source account won the race between this
            // transaction's marker check above and its insert here. Replay whichever attempt
            // actually committed instead of reporting an unrelated-looking Conflict (transaction
            // rules 4 and 8).
            await transaction.RollbackAsync(cancellationToken);

            var winner = await _context.Set<CloudAccountLinkIdempotencyRecord>().AsNoTracking()
                .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
            if (winner is not null)
            {
                return ReplayOutcome(winner);
            }

            return CloudAccountLinkOutcome.Rejected(CloudAccountLinkRejectionCode.SourceAlreadyLinked);
        }

        // AUTH-005: "Linking transfers ownership of the linked account's existing Cloud Inventory to
        // the Main Account" -- every eligible asset, atomically, since this bulk reassignment commits
        // in the same transaction as the link itself.
        var sourceOwnerId = CloudOwnerIdentity.ForAccount(shardId, sourceAccountId);
        var mainOwnerId = CloudOwnerIdentity.ForAccount(shardId, mainAccountId);
        await ReassignCloudOwnershipAsync(sourceOwnerId, mainOwnerId, cancellationToken);

        _context.Add(new CloudAccountLinkLedgerEvent(
            correlationId, shardId, CloudAccountLinkLedgerEventType.Linked, mainAccountId, sourceAccountId, reason: null));
        _context.Add(new CloudAccountLinkIdempotencyRecord(
            idempotencyKey, shardId, CloudAccountLinkOperationType.Link, mainAccountId, sourceAccountId,
            isApproved: true, CloudAccountLinkRejectionCode.None, link.Id, group.Id, correlationId));
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return CloudAccountLinkOutcome.Approved(link.Id, group.Id);
    }

    /// <summary>
    /// Ends an active link (AUTH-005): from this commit on, <paramref name="linkedAccountId"/>'s
    /// future deposits no longer route to <paramref name="mainAccountId"/>. Never touches any
    /// already-transferred Cloud asset. Repeating this call with the same
    /// <paramref name="idempotencyKey"/> replays the original committed result.
    /// </summary>
    public async Task<CloudAccountLinkOutcome> UnlinkAsync(
        string shardId,
        uint mainAccountId,
        uint linkedAccountId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Unlinking an account requires a Cloud Shard ID.", nameof(shardId));
        }

        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("Unlinking an account requires an idempotency key.", nameof(idempotencyKey));
        }

        _context.ChangeTracker.Clear();

        var existing = await _context.Set<CloudAccountLinkIdempotencyRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return ReplayOutcome(existing);
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var marker = await LockActiveLinkMarkerAsync(shardId, linkedAccountId, cancellationToken);
        var group = marker is null
            ? null
            : await _context.Set<CloudOwnershipGroup>().AsNoTracking().SingleOrDefaultAsync(g => g.Id == marker.OwnershipGroupId, cancellationToken);

        var linkIsActive = marker is not null && group is not null && group.MainAccountId == mainAccountId;
        var decision = CloudAccountLinkPolicy.EvaluateUnlink(linkIsActive, CloudMutationGateState.Open);
        var correlationId = Guid.NewGuid();

        if (!decision.IsApproved)
        {
            _context.Add(new CloudAccountLinkLedgerEvent(
                correlationId, shardId, CloudAccountLinkLedgerEventType.UnlinkRejected, mainAccountId, linkedAccountId, decision.RejectionCode.ToString()));
            _context.Add(new CloudAccountLinkIdempotencyRecord(
                idempotencyKey, shardId, CloudAccountLinkOperationType.Unlink, mainAccountId, linkedAccountId,
                isApproved: false, decision.RejectionCode, accountLinkId: null, ownershipGroupId: null, correlationId));
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CloudAccountLinkOutcome.Rejected(decision.RejectionCode);
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);

        var link = await _context.Set<CloudAccountLink>().SingleAsync(l => l.Id == marker!.AccountLinkId, cancellationToken);
        link.Unlink(nowUtc);
        _context.Update(link);
        _context.Remove(marker!);

        _context.Add(new CloudAccountLinkLedgerEvent(
            correlationId, shardId, CloudAccountLinkLedgerEventType.Unlinked, mainAccountId, linkedAccountId, reason: null));
        _context.Add(new CloudAccountLinkIdempotencyRecord(
            idempotencyKey, shardId, CloudAccountLinkOperationType.Unlink, mainAccountId, linkedAccountId,
            isApproved: true, CloudAccountLinkRejectionCode.None, link.Id, link.OwnershipGroupId, correlationId));
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return CloudAccountLinkOutcome.Approved(link.Id, link.OwnershipGroupId);
    }

    /// <summary>
    /// Returns the committed result of a link/unlink previously started with
    /// <paramref name="idempotencyKey"/>, or null if none has committed yet (transaction rule 8).
    /// </summary>
    public async Task<CloudAccountLinkOutcome?> TryGetLinkOutcomeAsync(Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Set<CloudAccountLinkIdempotencyRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);
        return existing is null ? null : ReplayOutcome(existing);
    }

    /// <summary>
    /// Resolves the account whose Cloud owner identity a new deposit from <paramref name="accountId"/>
    /// should use (AUTH-005): the group's Main Account ID if <paramref name="accountId"/> is
    /// currently an active Linked Account, otherwise <paramref name="accountId"/> itself. This is a
    /// plain (unlocked) read, not part of the deposit's own transaction -- see
    /// <c>Player_CloudCustodian.PrepareCloudDepositRow</c>'s call site for the residual race this
    /// leaves against a concurrent link/unlink of the same account, and why closing it fully belongs
    /// to moving this resolution inside <see cref="CloudCustodyBoundary"/>'s own locked deposit
    /// transaction rather than duplicating that transaction's shape here.
    /// </summary>
    public async Task<uint> ResolveEffectiveOwnerAccountIdAsync(string shardId, uint accountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Resolving an effective owner account requires a Cloud Shard ID.", nameof(shardId));
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Resolving an effective owner account requires a real account ID.");
        }

        var marker = await _context.Set<CloudActiveAccountLinkMarker>().AsNoTracking()
            .SingleOrDefaultAsync(m => m.ShardId == shardId && m.AccountId == accountId, cancellationToken);
        if (marker is null)
        {
            return accountId;
        }

        var group = await _context.Set<CloudOwnershipGroup>().AsNoTracking()
            .SingleAsync(g => g.Id == marker.OwnershipGroupId, cancellationToken);
        return group.MainAccountId;
    }

    private async Task<CloudActiveAccountLinkMarker?> LockActiveLinkMarkerAsync(string shardId, uint accountId, CancellationToken cancellationToken) =>
        await _context.Set<CloudActiveAccountLinkMarker>()
            .FromSqlInterpolated($"SELECT * FROM CloudActiveAccountLinkMarker WHERE ShardId = {shardId} AND AccountId = {accountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<bool> SourceHasActiveChildrenAsync(string shardId, uint sourceAccountId, CancellationToken cancellationToken)
    {
        var sourceGroup = await _context.Set<CloudOwnershipGroup>().AsNoTracking()
            .SingleOrDefaultAsync(g => g.ShardId == shardId && g.MainAccountId == sourceAccountId, cancellationToken);
        if (sourceGroup is null)
        {
            return false;
        }

        return await _context.Set<CloudActiveAccountLinkMarker>().AsNoTracking()
            .AnyAsync(m => m.OwnershipGroupId == sourceGroup.Id, cancellationToken);
    }

    /// <summary>
    /// AUTH-006's "free of active reservations... tokens... or other in-flight obligations", scoped
    /// to the obligation types that exist in this schema today (Withdrawal Tokens). See this
    /// gateway's class doc comment for the listing/bid/settlement/offer types not yet implemented.
    /// </summary>
    private async Task<bool> SourceHasPendingObligationsAsync(string shardId, uint sourceAccountId, CancellationToken cancellationToken)
    {
        var sourceOwnerId = CloudOwnerIdentity.ForAccount(shardId, sourceAccountId);

        var hasActiveWithdrawal = await _context.CloudWithdrawalReservations.AsNoTracking()
            .AnyAsync(r => r.OwnerId == sourceOwnerId && r.Status == CloudReservationStatus.Active, cancellationToken);
        if (hasActiveWithdrawal)
        {
            return true;
        }

        return await _context.CloudStackLotWithdrawalReservations.AsNoTracking()
            .AnyAsync(r => r.OwnerId == sourceOwnerId && r.Status == CloudReservationStatus.Active, cancellationToken);
    }

    private async Task ReassignCloudOwnershipAsync(Guid sourceOwnerId, Guid mainOwnerId, CancellationToken cancellationToken)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE CloudCustodyRecord SET OwnerId = {mainOwnerId}, Version = Version + 1 WHERE OwnerId = {sourceOwnerId} AND TotalQuantity IS NULL",
            cancellationToken);

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE CloudStackLot SET OwnerId = {mainOwnerId}, Version = Version + 1 WHERE OwnerId = {sourceOwnerId}",
            cancellationToken);
    }

    private static CloudAccountLinkOutcome ReplayOutcome(CloudAccountLinkIdempotencyRecord existing) =>
        existing.IsApproved
            ? CloudAccountLinkOutcome.Approved(existing.AccountLinkId!.Value, existing.OwnershipGroupId!.Value)
            : CloudAccountLinkOutcome.Rejected(existing.RejectionCode);

    private async Task<DateTime> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT UTC_TIMESTAMP(6);";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return DateTime.SpecifyKind(Convert.ToDateTime(result), DateTimeKind.Utc);
    }
}
