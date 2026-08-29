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
    ///
    /// Issue #20's Red section requires "concurrent link... and retry": two concurrent LinkAsync
    /// calls for the *same* source account into two different Mains both take
    /// <see cref="LockActiveLinkMarkerAsync"/>'s gap lock on that not-yet-existing marker row before
    /// either commits, then one of them tries to insert it while <see cref="LockSourceCustodyRowsAsync"/>
    /// still holds the other's custody-row locks -- a genuine MariaDB deadlock (error 1213) that
    /// deterministic lock ordering alone cannot prevent, matching <see cref="CloudBoundaryRetry"/>'s
    /// own doc comment. Retrying here, exactly like every <c>CloudCustodyBoundary</c> call site,
    /// re-runs the whole rolled-back attempt from scratch, which is safe: the deadlock always aborts
    /// before <see cref="LinkOnceAsync"/> commits anything, and the idempotency key makes a retry
    /// that races a since-committed attempt replay that result instead of double-linking.
    /// </summary>
    public Task<CloudAccountLinkOutcome> LinkAsync(
        string shardId,
        uint mainAccountId,
        uint sourceAccountId,
        Guid idempotencyKey,
        bool wouldCreateActiveAuctionConflict = false,
        CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteWithDeadlockRetryAsync(
            () => LinkOnceAsync(shardId, mainAccountId, sourceAccountId, idempotencyKey, wouldCreateActiveAuctionConflict, cancellationToken),
            cancellationToken: cancellationToken);

    private async Task<CloudAccountLinkOutcome> LinkOnceAsync(
        string shardId,
        uint mainAccountId,
        uint sourceAccountId,
        Guid idempotencyKey,
        bool wouldCreateActiveAuctionConflict,
        CancellationToken cancellationToken)
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

        // Locks every Cloud Custody Record/Stack Lot the source currently owns for the rest of this
        // transaction, before the pending-obligations check below reads them (transaction rule 2).
        // CloudCustodyBoundary.ReserveForWithdrawalAsync locks that same row before opening a
        // reservation over any target -- whole item or stack lot -- so this makes the two operations
        // mutually exclusive: a reservation attempt racing this link either already committed (and is
        // visible to the obligations check below) or blocks here until this transaction
        // commits/rolls back -- closing the window where a reservation opened between an unlocked
        // obligations read and the later bulk reassignment could be silently orphaned by it.
        await LockSourceCustodyRowsAsync(shardId, sourceAccountId, cancellationToken);

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

        // Locks the Main Account's own CloudOwnershipGroup row before deciding whether to create it,
        // mirroring LockActiveLinkMarkerAsync's pattern: an unlocked read is invisible to a concurrent
        // LinkAsync's not-yet-committed insert of the same row, so both would try to create it and one
        // would misreport a duplicate-key collision on UQ_CloudOwnershipGroup_Shard_Main as
        // SourceAlreadyLinked. This locked read instead waits for that in-flight insert to commit and
        // reuses the row it created.
        var group = await LockOwnershipGroupAsync(shardId, mainAccountId, cancellationToken);
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

    /// <summary>
    /// Returns every ACE account ID currently in the same ownership group as
    /// <paramref name="accountId"/> -- its Main Account plus every other currently-active Linked
    /// Account -- or just <paramref name="accountId"/> itself if it is neither an active Linked
    /// Account nor a Main Account with any active children. Unlike
    /// <see cref="ResolveEffectiveOwnerAccountIdAsync"/> (which only answers "where do this account's
    /// own future deposits route"), this also covers the Main-Account-side membership check a caller
    /// needs to decide "does this identity belong to *my* group at all" without ever comparing raw
    /// ACE account IDs or a precomputed <see cref="CloudOwnerIdentity"/> directly -- see
    /// <c>Player_CloudWithdrawal.RedeemAsync</c>'s Withdrawal Token ownership check, which must accept
    /// a token whose reservation was opened under either the Main Account's or a Linked Account's
    /// identity once the two are linked (CONTEXT.md: "redeemed by any character currently belonging
    /// to the Main Account or one of its Linked Accounts").
    /// </summary>
    public async Task<IReadOnlyCollection<uint>> GetOwnershipGroupAccountIdsAsync(string shardId, uint accountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Resolving an ownership group requires a Cloud Shard ID.", nameof(shardId));
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "Resolving an ownership group requires a real account ID.");
        }

        var marker = await _context.Set<CloudActiveAccountLinkMarker>().AsNoTracking()
            .SingleOrDefaultAsync(m => m.ShardId == shardId && m.AccountId == accountId, cancellationToken);

        Guid groupId;
        uint mainAccountId;
        if (marker is not null)
        {
            var group = await _context.Set<CloudOwnershipGroup>().AsNoTracking()
                .SingleAsync(g => g.Id == marker.OwnershipGroupId, cancellationToken);
            groupId = group.Id;
            mainAccountId = group.MainAccountId;
        }
        else
        {
            var group = await _context.Set<CloudOwnershipGroup>().AsNoTracking()
                .SingleOrDefaultAsync(g => g.ShardId == shardId && g.MainAccountId == accountId, cancellationToken);
            if (group is null)
            {
                return new[] { accountId };
            }

            groupId = group.Id;
            mainAccountId = accountId;
        }

        var linkedAccountIds = await _context.Set<CloudActiveAccountLinkMarker>().AsNoTracking()
            .Where(m => m.OwnershipGroupId == groupId)
            .Select(m => m.AccountId)
            .ToListAsync(cancellationToken);

        linkedAccountIds.Add(mainAccountId);
        return linkedAccountIds;
    }

    private async Task<CloudActiveAccountLinkMarker?> LockActiveLinkMarkerAsync(string shardId, uint accountId, CancellationToken cancellationToken) =>
        await _context.Set<CloudActiveAccountLinkMarker>()
            .FromSqlInterpolated($"SELECT * FROM CloudActiveAccountLinkMarker WHERE ShardId = {shardId} AND AccountId = {accountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Locks <paramref name="mainAccountId"/>'s own CloudOwnershipGroup row for the (ShardId,
    /// MainAccountId) key covered by <c>UQ_CloudOwnershipGroup_Shard_Main</c>. If a concurrent caller
    /// has already inserted that row but not yet committed, this blocks until it does, then returns
    /// the committed row -- unlike a plain read, which cannot see an uncommitted insert and would
    /// wrongly conclude the row still needs to be created.
    /// </summary>
    private async Task<CloudOwnershipGroup?> LockOwnershipGroupAsync(string shardId, uint mainAccountId, CancellationToken cancellationToken) =>
        await _context.Set<CloudOwnershipGroup>()
            .FromSqlInterpolated($"SELECT * FROM CloudOwnershipGroup WHERE ShardId = {shardId} AND MainAccountId = {mainAccountId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task LockSourceCustodyRowsAsync(string shardId, uint sourceAccountId, CancellationToken cancellationToken)
    {
        var sourceOwnerId = CloudOwnerIdentity.ForAccount(shardId, sourceAccountId);

        await _context.Set<CloudCustodyRecord>()
            .FromSqlInterpolated($"SELECT * FROM CloudCustodyRecord WHERE OwnerId = {sourceOwnerId} AND TotalQuantity IS NULL FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        await _context.Set<CloudStackLot>()
            .FromSqlInterpolated($"SELECT * FROM CloudStackLot WHERE OwnerId = {sourceOwnerId} FOR UPDATE")
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> SourceHasActiveChildrenAsync(string shardId, uint sourceAccountId, CancellationToken cancellationToken)
    {
        // Locked (not a plain read) so this decision waits for a concurrent LinkAsync that is making
        // sourceAccountId itself a Main with a new child -- that call resolves this exact (ShardId,
        // MainAccountId) row too, via LockOwnershipGroupAsync -- to commit or roll back, instead of an
        // unlocked read missing its not-yet-committed insert and letting the forbidden 3-level tree
        // form (AUTH-006: trees/whole-group merges are prohibited).
        var sourceGroup = await LockOwnershipGroupAsync(shardId, sourceAccountId, cancellationToken);
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

        return await _context.CloudWithdrawalReservations.AsNoTracking()
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
