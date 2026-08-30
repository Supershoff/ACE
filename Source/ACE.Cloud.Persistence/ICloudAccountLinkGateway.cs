using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The account-linking capabilities issue #33's HTTP surface needs from <see cref="CloudAccountLinkGateway"/>
/// (AUTH-004..009), interface-extracted so <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory
/// fake instead of a real MariaDB-backed <see cref="CloudDbContext"/>, mirroring
/// <see cref="ICloudAccountOwnershipResolver"/>'s existing precedent.
/// </summary>
public interface ICloudAccountLinkGateway : ICloudAccountOwnershipResolver
{
    Task<CloudAccountLinkOutcome> LinkAsync(
        string shardId,
        uint mainAccountId,
        uint sourceAccountId,
        Guid idempotencyKey,
        bool wouldCreateActiveAuctionConflict = false,
        CancellationToken cancellationToken = default);

    Task<CloudAccountLinkOutcome> UnlinkAsync(
        string shardId,
        uint mainAccountId,
        uint linkedAccountId,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<uint>> GetOwnershipGroupAccountIdsAsync(
        string shardId, uint accountId, CancellationToken cancellationToken = default);

    /// <summary>The Main Account's <see cref="CloudOwnershipGroup"/> ID, or null if it has never linked/been linked.</summary>
    Task<Guid?> TryGetOwnershipGroupIdAsync(string shardId, uint mainAccountId, CancellationToken cancellationToken = default);
}
