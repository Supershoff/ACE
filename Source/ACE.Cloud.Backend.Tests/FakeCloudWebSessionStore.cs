using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// An in-memory <see cref="ICloudWebSessionStore"/> substitute that still enforces one-use nonces
/// and expiry/revocation semantics, so Backend endpoint tests exercise real session-security
/// behavior without requiring a real MariaDB (the real enforcement -- a unique database constraint
/// -- is proven separately by ACE.Cloud.PersistenceIntegrationTests.CloudSessionGatewayTests).
/// </summary>
internal sealed class FakeCloudWebSessionStore : ICloudWebSessionStore
{
    private readonly HashSet<Guid> _consumedNonces = [];
    private readonly Dictionary<string, CloudWebSession> _sessionsBySecretHash = [];

    public Task<CloudSessionExchangeResult> ExchangeGrantForSessionAsync(
        string shardId,
        uint accountId,
        Guid grantNonce,
        string secretHash,
        string csrfToken,
        DateTime nowUtc,
        TimeSpan sessionTimeToLive,
        CancellationToken cancellationToken = default)
    {
        if (!_consumedNonces.Add(grantNonce))
        {
            return Task.FromResult(CloudSessionExchangeResult.GrantAlreadyUsed());
        }

        var session = CloudWebSession.Open(shardId, accountId, secretHash, csrfToken, nowUtc, nowUtc + sessionTimeToLive);
        _sessionsBySecretHash[secretHash] = session;

        return Task.FromResult(CloudSessionExchangeResult.Created(session));
    }

    public Task<CloudWebSession?> TryGetActiveSessionAsync(string secretHash, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!_sessionsBySecretHash.TryGetValue(secretHash, out var session) || !session.IsActiveAt(nowUtc))
        {
            return Task.FromResult<CloudWebSession?>(null);
        }

        return Task.FromResult<CloudWebSession?>(session);
    }

    public Task RevokeSessionAsync(string secretHash, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        _sessionsBySecretHash.Remove(secretHash);
        return Task.CompletedTask;
    }
}
