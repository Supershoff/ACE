namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud backend's own authoritative record of one secure web session (AUTH-002's "Exchange
/// grants in the backend for secure HttpOnly SameSite sessions"). <see cref="SecretHash"/> stores a
/// one-way verifier of the session cookie's random secret (security baseline: "store a one-way
/// verifier if practical; compare safely"), never the secret itself -- the same pattern
/// <c>CloudWithdrawalReservation.TokenHash</c> already uses for Withdrawal Tokens.
/// </summary>
public sealed class CloudWebSession
{
    private CloudWebSession()
    {
    }

    private CloudWebSession(
        Guid id,
        string shardId,
        uint accountId,
        string secretHash,
        string csrfToken,
        Guid? rotatedFromSessionId,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        DateTime lastSeenAtUtc,
        DateTime? revokedAtUtc)
    {
        Id = id;
        ShardId = shardId;
        AccountId = accountId;
        SecretHash = secretHash;
        CsrfToken = csrfToken;
        RotatedFromSessionId = rotatedFromSessionId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        LastSeenAtUtc = lastSeenAtUtc;
        RevokedAtUtc = revokedAtUtc;
    }

    public static CloudWebSession Open(
        string shardId,
        uint accountId,
        string secretHash,
        string csrfToken,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        Guid? rotatedFromSessionId = null)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A web session requires a Cloud Shard ID.", nameof(shardId));
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A web session requires a real ACE account ID.");
        }

        if (string.IsNullOrWhiteSpace(secretHash))
        {
            throw new ArgumentException("A web session requires a secret hash.", nameof(secretHash));
        }

        if (string.IsNullOrWhiteSpace(csrfToken))
        {
            throw new ArgumentException("A web session requires a CSRF token.", nameof(csrfToken));
        }

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "A web session's expiry must be after its creation time.");
        }

        return new CloudWebSession(
            Guid.NewGuid(), shardId, accountId, secretHash, csrfToken, rotatedFromSessionId,
            createdAtUtc, expiresAtUtc, lastSeenAtUtc: createdAtUtc, revokedAtUtc: null);
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public uint AccountId { get; private set; }

    public string SecretHash { get; private set; } = null!;

    public string CsrfToken { get; private set; } = null!;

    /// <summary>The prior session this one replaced, if opened by <c>CloudSessionGateway.RotateSessionAsync</c>.</summary>
    public Guid? RotatedFromSessionId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime LastSeenAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public bool IsActiveAt(DateTime nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    internal void Touch(DateTime nowUtc) => LastSeenAtUtc = nowUtc;

    internal void Revoke(DateTime revokedAtUtc)
    {
        if (RevokedAtUtc is not null)
        {
            throw new InvalidOperationException($"Web session {Id} was already revoked.");
        }

        RevokedAtUtc = revokedAtUtc;
    }
}
