namespace ACE.Cloud.Persistence;

/// <summary>
/// The session-management surface the Cloud backend's HTTP endpoints depend on (AUTH-002),
/// implemented by <see cref="CloudSessionGateway"/> against the real <see cref="CloudDbContext"/>.
/// Exists as its own seam so endpoint tests can substitute a fast in-memory double instead of a
/// real MariaDB (Refactor bullet: "retain focused seams for fault injection and authorization
/// tests").
/// </summary>
public interface ICloudWebSessionStore
{
    Task<CloudSessionExchangeResult> ExchangeGrantForSessionAsync(
        string shardId,
        uint accountId,
        Guid grantNonce,
        string secretHash,
        string csrfToken,
        DateTime nowUtc,
        TimeSpan sessionTimeToLive,
        CancellationToken cancellationToken = default);

    Task<CloudWebSession?> TryGetActiveSessionAsync(string secretHash, DateTime nowUtc, CancellationToken cancellationToken = default);

    Task RevokeSessionAsync(string secretHash, DateTime nowUtc, CancellationToken cancellationToken = default);
}
