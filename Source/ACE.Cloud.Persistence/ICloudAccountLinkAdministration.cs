namespace ACE.Cloud.Persistence;

/// <summary>One active Linked Account under a Main Account's ownership group, as exposed to the account identity/linking HTTP surface (AUTH-005).</summary>
public sealed record CloudAccountLinkSummary(uint LinkedAccountId, DateTime LinkedAtUtc);

/// <summary>
/// The <see cref="CloudAccountLinkGateway"/> capabilities issue #33's account identity/linking HTTP
/// endpoints need beyond <see cref="ICloudAccountOwnershipResolver"/>'s single read (AUTH-003,
/// AUTH-005..009). Interface-extracted for the same reason as
/// <see cref="ICloudAccountOwnershipResolver"/>: so <c>ACE.Cloud.Backend.Tests</c> can substitute an
/// in-memory fake instead of standing up a real MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudAccountLinkAdministration
{
    Task<CloudAccountLinkOutcome> LinkAsync(
        string shardId,
        uint mainAccountId,
        uint sourceAccountId,
        Guid idempotencyKey,
        bool wouldCreateActiveAuctionConflict = false,
        CancellationToken cancellationToken = default);

    Task<CloudAccountLinkOutcome> UnlinkAsync(
        string shardId, uint mainAccountId, uint linkedAccountId, Guid idempotencyKey, CancellationToken cancellationToken = default);

    /// <summary>Every currently active Linked Account under <paramref name="mainAccountId"/>'s ownership group, or an empty list if it has none (or is not itself a Main Account).</summary>
    Task<IReadOnlyList<CloudAccountLinkSummary>> GetActiveLinksAsync(
        string shardId, uint mainAccountId, CancellationToken cancellationToken = default);

    /// <summary>The ownership group ID backing <paramref name="mainAccountId"/>, or null if it has never linked any account (no group has been created yet).</summary>
    Task<Guid?> TryGetOwnershipGroupIdAsync(string shardId, uint mainAccountId, CancellationToken cancellationToken = default);
}
