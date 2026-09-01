using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The <see cref="CloudSharingGrantGateway"/> mutation capability issue #39's Sharing Grant web
/// endpoints need (SHARE-001..004). Interface-extracted for the same reason as
/// <see cref="ICloudTransferOfferService"/>: so <c>ACE.Cloud.Backend.Tests</c> can substitute an
/// in-memory fake instead of standing up a real MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudSharingGrantService
{
    Task<CloudBoundaryOutcome<CloudSharingGrantRecord>> SetAsync(
        string shardId,
        uint ownerAccountId,
        string granteeCharacterName,
        CloudSharingGrantLevel requestedLevel,
        CancellationToken cancellationToken = default);
}
