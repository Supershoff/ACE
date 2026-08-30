namespace ACE.Cloud.Persistence;

/// <summary>
/// The one read <see cref="CloudAccountLinkGateway"/> capability a session-authorization check needs
/// (AUTH-004/AUTH-005: "where do this account's deposits currently route"). Interface-extracted
/// (mirroring <see cref="ICloudWebSessionStore"/>) so <c>ACE.Cloud.Backend.Tests</c> can substitute an
/// in-memory fake for endpoint tests instead of standing up a real MariaDB-backed
/// <see cref="CloudDbContext"/> just to answer this one query.
/// </summary>
public interface ICloudAccountOwnershipResolver
{
    Task<uint> ResolveEffectiveOwnerAccountIdAsync(string shardId, uint accountId, CancellationToken cancellationToken = default);
}
