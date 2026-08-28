using System.Security.Cryptography;

namespace ACE.Cloud.Domain;

public enum CloudAuthGrantValidationOutcomeKind
{
    Valid,
    Malformed,
    UnknownSigningKey,
    BadSignature,
    Expired,
    AudienceMismatch,
}

public sealed record CloudAuthGrantValidationResult(CloudAuthGrantValidationOutcomeKind Kind, CloudAuthGrant? Grant)
{
    public bool IsValid => Kind == CloudAuthGrantValidationOutcomeKind.Valid;

    public static CloudAuthGrantValidationResult Valid(CloudAuthGrant grant) => new(CloudAuthGrantValidationOutcomeKind.Valid, grant);

    public static CloudAuthGrantValidationResult Invalid(CloudAuthGrantValidationOutcomeKind kind) => new(kind, Grant: null);
}

/// <summary>
/// Verifies a token <see cref="CloudAuthGrantIssuer"/> produced (AUTH-002): signature, expiry, and
/// audience, checking against both the ring's <see cref="CloudPrivateServiceKeyRing.ActiveKey"/> and
/// its optional <see cref="CloudPrivateServiceKeyRing.PreviousKey"/> so a grant signed just before a
/// key rotation still validates during the deployment's overlap window. A consumer must still
/// separately enforce one-use by recording <see cref="CloudAuthGrant.Nonce"/> consumption itself
/// (this type has no persistence of its own).
/// </summary>
public static class CloudAuthGrantValidator
{
    public static CloudAuthGrantValidationResult Validate(string? token, string expectedAudience, DateTime nowUtc, CloudPrivateServiceKeyRing keyRing)
    {
        if (string.IsNullOrWhiteSpace(expectedAudience))
        {
            throw new ArgumentException("An expected audience is required.", nameof(expectedAudience));
        }

        ArgumentNullException.ThrowIfNull(keyRing);

        if (!CloudAuthGrantCodec.TryDecompose(token, out var payload, out var keyId, out var signature))
        {
            return CloudAuthGrantValidationResult.Invalid(CloudAuthGrantValidationOutcomeKind.Malformed);
        }

        if (!keyRing.TryGetKey(keyId, out var key))
        {
            return CloudAuthGrantValidationResult.Invalid(CloudAuthGrantValidationOutcomeKind.UnknownSigningKey);
        }

        var expectedSignature = CloudAuthGrantCodec.Sign(payload, key.Secret);
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, signature))
        {
            return CloudAuthGrantValidationResult.Invalid(CloudAuthGrantValidationOutcomeKind.BadSignature);
        }

        if (!CloudAuthGrantCodec.TryParsePayload(payload, out var accountId, out var audience, out var issuedAtUtc, out var expiresAtUtc, out var nonce))
        {
            return CloudAuthGrantValidationResult.Invalid(CloudAuthGrantValidationOutcomeKind.Malformed);
        }

        if (nowUtc >= expiresAtUtc)
        {
            return CloudAuthGrantValidationResult.Invalid(CloudAuthGrantValidationOutcomeKind.Expired);
        }

        if (!string.Equals(audience, expectedAudience, StringComparison.Ordinal))
        {
            return CloudAuthGrantValidationResult.Invalid(CloudAuthGrantValidationOutcomeKind.AudienceMismatch);
        }

        return CloudAuthGrantValidationResult.Valid(new CloudAuthGrant(accountId, audience, issuedAtUtc, expiresAtUtc, nonce));
    }
}
