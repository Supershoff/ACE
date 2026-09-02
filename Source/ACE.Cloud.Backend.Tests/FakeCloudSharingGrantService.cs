using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// An in-memory <see cref="ICloudSharingGrantService"/>/<see cref="ICloudSharingGrantReader"/>
/// substitute exercising the same "set is an idempotent upsert" and self/unknown-grantee rejection
/// shape as the real gateway, so Sharing Grant endpoint tests exercise real routing/authorization
/// without requiring a real MariaDB (the full authorization-composition and revocation-on-authority-
/// loss behavior is proven separately by
/// <c>ACE.Cloud.PersistenceIntegrationTests.CloudSharingGrantGatewayTests</c>).
/// </summary>
internal sealed class FakeCloudSharingGrantService : ICloudSharingGrantService, ICloudSharingGrantReader
{
    private readonly Dictionary<(Guid Owner, Guid Grantee), CloudSharingGrantRecord> _grantsByOwnerAndGrantee = [];

    /// <summary>Maps a typed grantee character name to its owning account's effective owner Guid, mirroring the real gateway's SHARE-001 resolution.</summary>
    public Dictionary<string, Guid> GranteeOwnerIdsByCharacterName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<CloudBoundaryOutcome<CloudSharingGrantRecord>> SetAsync(
        string shardId, uint ownerAccountId, string granteeCharacterName, CloudSharingGrantLevel requestedLevel,
        CancellationToken cancellationToken = default)
    {
        if (!GranteeOwnerIdsByCharacterName.TryGetValue(granteeCharacterName, out var granteeId))
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudSharingGrantRecord>.Conflict($"Unknown grantee character '{granteeCharacterName}'."));
        }

        var ownerId = CloudOwnerIdentity.ForAccount(shardId, ownerAccountId);
        if (ownerId == granteeId)
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudSharingGrantRecord>.Conflict("A Sharing Grant cannot target the owner's own account."));
        }

        var key = (ownerId, granteeId);
        var nowUtc = DateTime.UtcNow;
        if (_grantsByOwnerAndGrantee.TryGetValue(key, out var existing))
        {
            existing.TrySetLevel(requestedLevel, nowUtc);
            return Task.FromResult(CloudBoundaryOutcome<CloudSharingGrantRecord>.Committed(existing));
        }

        var grant = CloudSharingGrantRecord.Open(Guid.NewGuid(), shardId, ownerId, granteeId, requestedLevel, nowUtc);
        _grantsByOwnerAndGrantee[key] = grant;
        return Task.FromResult(CloudBoundaryOutcome<CloudSharingGrantRecord>.Committed(grant));
    }

    public Task<CloudSharingAccessLevel> GetEffectiveAccessAsync(
        string shardId, uint ownerAccountId, uint viewerAccountId, CancellationToken cancellationToken = default)
    {
        if (ownerAccountId == viewerAccountId)
        {
            return Task.FromResult(CloudSharingAccessLevel.Owner);
        }

        var ownerId = CloudOwnerIdentity.ForAccount(shardId, ownerAccountId);
        var viewerId = CloudOwnerIdentity.ForAccount(shardId, viewerAccountId);
        var level = _grantsByOwnerAndGrantee.TryGetValue((ownerId, viewerId), out var grant) ? grant.Level : (CloudSharingGrantLevel?)null;

        return Task.FromResult(level switch
        {
            CloudSharingGrantLevel.ViewAndWithdraw => CloudSharingAccessLevel.ViewAndWithdraw,
            CloudSharingGrantLevel.ViewOnly => CloudSharingAccessLevel.ViewOnly,
            _ => CloudSharingAccessLevel.None,
        });
    }

    public Task<IReadOnlyList<Guid>> GetAuthorizedGrantorOwnerIdsAsync(
        string shardId, uint viewerAccountId, CancellationToken cancellationToken = default)
    {
        var viewerId = CloudOwnerIdentity.ForAccount(shardId, viewerAccountId);
        return Task.FromResult<IReadOnlyList<Guid>>(_grantsByOwnerAndGrantee.Values
            .Where(g => g.GranteeId == viewerId && g.Level != CloudSharingGrantLevel.None)
            .Select(g => g.OwnerId)
            .Distinct()
            .ToList());
    }

    public Task<IReadOnlyList<CloudSharingGrantRecord>> GetGivenAsync(string shardId, Guid ownerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudSharingGrantRecord>>(
            _grantsByOwnerAndGrantee.Values.Where(g => g.ShardId == shardId && g.OwnerId == ownerId).ToList());

    public Task<IReadOnlyList<CloudSharingGrantRecord>> GetReceivedAsync(string shardId, Guid granteeId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudSharingGrantRecord>>(
            _grantsByOwnerAndGrantee.Values.Where(g => g.ShardId == shardId && g.GranteeId == granteeId).ToList());
}
