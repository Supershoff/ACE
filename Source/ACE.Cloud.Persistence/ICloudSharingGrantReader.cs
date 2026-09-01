using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Interface-extracted (mirroring <see cref="ICloudCharacterAllegianceVaultReader"/>) so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake instead of standing up a real
/// MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudSharingGrantReader
{
    /// <summary>
    /// The effective access <paramref name="viewerAccountId"/> currently has to
    /// <paramref name="ownerAccountId"/>'s personal Cloud Inventory (SHARE-004). Both account IDs
    /// must already be resolved to their effective Main Account (mirrors every other Cloud Transaction
    /// Authority call site's own established discipline: <see cref="ICloudAccountOwnershipResolver"/>
    /// resolves the group before this reader ever sees an account ID).
    /// </summary>
    Task<CloudSharingAccessLevel> GetEffectiveAccessAsync(
        string shardId, uint ownerAccountId, uint viewerAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every owner identity that currently authorizes <paramref name="viewerAccountId"/> at least
    /// View Only access to their personal Cloud Inventory -- an explicit grant at any non-None level,
    /// or guild-derived access not explicitly overridden to None -- for composing into
    /// <see cref="CloudLiveStreamViewer.AuthorizedOwnerIds"/> (mirrors
    /// <see cref="CloudCharacterAllegianceVaultReader.GetCurrentAllegianceVaultOwnerIdsAsync"/>'s own
    /// established shape and the forward reference <see cref="CloudActivityLedgerQueryEngine"/>'s own
    /// doc comment already anticipates: "a caller resolving a Shared-scoped view will do the same
    /// once Sharing Grants ... exist to supply grantor owner IDs").
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAuthorizedGrantorOwnerIdsAsync(
        string shardId, uint viewerAccountId, CancellationToken cancellationToken = default);
}

/// <summary>See <see cref="ICloudSharingGrantReader"/>.</summary>
public sealed class CloudSharingGrantReader : ICloudSharingGrantReader
{
    private readonly CloudDbContext _context;

    public CloudSharingGrantReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CloudSharingAccessLevel> GetEffectiveAccessAsync(
        string shardId, uint ownerAccountId, uint viewerAccountId, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        RequireAccountId(ownerAccountId, nameof(ownerAccountId));
        RequireAccountId(viewerAccountId, nameof(viewerAccountId));

        if (ownerAccountId == viewerAccountId)
        {
            return CloudSharingAccessLevel.Owner;
        }

        var ownerId = CloudOwnerIdentity.ForAccount(shardId, ownerAccountId);
        var viewerId = CloudOwnerIdentity.ForAccount(shardId, viewerAccountId);

        var explicitGrant = await _context.CloudSharingGrants.AsNoTracking()
            .SingleOrDefaultAsync(g => g.ShardId == shardId && g.OwnerId == ownerId && g.GranteeId == viewerId, cancellationToken);

        var hasQualifyingDerivedAccess = await HasSharedCurrentAllegianceAsync(shardId, ownerAccountId, viewerAccountId, cancellationToken);

        return CloudSharingGrantPolicy.ResolveEffectiveAccess(isOwner: false, explicitGrant?.Level, hasQualifyingDerivedAccess);
    }

    public async Task<IReadOnlyList<Guid>> GetAuthorizedGrantorOwnerIdsAsync(
        string shardId, uint viewerAccountId, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);
        RequireAccountId(viewerAccountId, nameof(viewerAccountId));

        var viewerId = CloudOwnerIdentity.ForAccount(shardId, viewerAccountId);

        var explicitGrants = await _context.CloudSharingGrants.AsNoTracking()
            .Where(g => g.ShardId == shardId && g.GranteeId == viewerId)
            .Select(g => new { g.OwnerId, g.Level })
            .ToListAsync(cancellationToken);

        var explicitlyDeniedOwnerIds = explicitGrants.Where(g => g.Level == CloudSharingGrantLevel.None).Select(g => g.OwnerId).ToHashSet();
        var authorized = explicitGrants.Where(g => g.Level != CloudSharingGrantLevel.None).Select(g => g.OwnerId).ToHashSet();

        var viewerMonarchIds = await GetCurrentMonarchIdsAsync(shardId, viewerAccountId, cancellationToken);
        if (viewerMonarchIds.Count > 0)
        {
            var derivedOwnerAccountIds = await _context.CloudCharacterIdentityReadProjections.AsNoTracking()
                .Where(row => row.ShardId == shardId && row.AccountId != null && row.AccountId != viewerAccountId
                    && row.MonarchId != null && viewerMonarchIds.Contains(row.MonarchId.Value))
                .Select(row => row.AccountId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);

            foreach (var derivedOwnerAccountId in derivedOwnerAccountIds)
            {
                var derivedOwnerId = CloudOwnerIdentity.ForAccount(shardId, derivedOwnerAccountId);
                if (!explicitlyDeniedOwnerIds.Contains(derivedOwnerId))
                {
                    authorized.Add(derivedOwnerId);
                }
            }
        }

        return authorized.ToList();
    }

    /// <summary>
    /// True when any character currently on <paramref name="ownerAccountId"/> shares a current
    /// allegiance (the same live <c>MonarchId</c>) with any character currently on
    /// <paramref name="viewerAccountId"/>, read from the same versioned identity/allegiance cache
    /// VAULT-001's own reader uses (never treated as authority in its own right).
    /// </summary>
    private async Task<bool> HasSharedCurrentAllegianceAsync(
        string shardId, uint ownerAccountId, uint viewerAccountId, CancellationToken cancellationToken)
    {
        var ownerMonarchIds = await GetCurrentMonarchIdsAsync(shardId, ownerAccountId, cancellationToken);
        if (ownerMonarchIds.Count == 0)
        {
            return false;
        }

        var viewerMonarchIds = await GetCurrentMonarchIdsAsync(shardId, viewerAccountId, cancellationToken);
        return viewerMonarchIds.Overlaps(ownerMonarchIds);
    }

    private async Task<HashSet<uint>> GetCurrentMonarchIdsAsync(string shardId, uint accountId, CancellationToken cancellationToken)
    {
        var monarchIds = await _context.CloudCharacterIdentityReadProjections.AsNoTracking()
            .Where(row => row.ShardId == shardId && row.AccountId == accountId && row.MonarchId != null)
            .Select(row => row.MonarchId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        return monarchIds.ToHashSet();
    }

    private static void RequireShardId(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Resolving Sharing Grant access requires a Cloud Shard ID.", nameof(shardId));
        }
    }

    private static void RequireAccountId(uint accountId, string paramName)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "Resolving Sharing Grant access requires a real account ID.");
        }
    }
}
