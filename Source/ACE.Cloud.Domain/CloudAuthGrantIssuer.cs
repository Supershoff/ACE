namespace ACE.Cloud.Domain;

/// <summary>
/// Issues signed, audience-bound, short-lived, one-use grants (AUTH-002). The token is a compact
/// self-contained credential -- not a database row -- because the Auth Bridge has no Cloud schema
/// access at all (ARCH-004) and so cannot itself track one-time-use state shared with the Cloud
/// backend; the backend instead records <see cref="CloudAuthGrant.Nonce"/> consumption in its own
/// schema once it independently verifies this signature (<see cref="CloudAuthGrantValidator"/>).
/// </summary>
public static class CloudAuthGrantIssuer
{
    public static string Issue(uint accountId, string audience, DateTime issuedAtUtc, TimeSpan timeToLive, CloudPrivateServiceKeyRing keyRing)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A grant requires a real ACE account ID.");
        }

        if (string.IsNullOrWhiteSpace(audience))
        {
            throw new ArgumentException("A grant requires a non-empty audience.", nameof(audience));
        }

        if (timeToLive <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeToLive), "A grant's time-to-live must be positive.");
        }

        ArgumentNullException.ThrowIfNull(keyRing);

        var expiresAtUtc = issuedAtUtc + timeToLive;
        var nonce = Guid.NewGuid();

        var payload = CloudAuthGrantCodec.EncodePayload(accountId, audience, issuedAtUtc, expiresAtUtc, nonce);
        var signature = CloudAuthGrantCodec.Sign(payload, keyRing.ActiveKey.Secret);

        return CloudAuthGrantCodec.ComposeToken(payload, keyRing.ActiveKey.KeyId, signature);
    }
}
