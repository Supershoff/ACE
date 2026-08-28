using System.Security.Cryptography;

namespace ACE.Cloud.Domain;

/// <summary>
/// A newly minted Withdrawal Token (WDR-001): <see cref="Secret"/> is the high-entropy value shown
/// to the player exactly once and typed/pasted into the redemption command; <see cref="Hash"/> is
/// the one-way verifier persisted instead of the secret itself (security baseline: "store a one-way
/// verifier if practical; compare safely").
/// </summary>
public sealed record CloudWithdrawalToken(string Secret, string Hash);

/// <summary>
/// Generates cryptographically strong, single-use-verifiable Withdrawal Tokens and their one-way
/// verifiers (WDR-001). A token's <see cref="CloudWithdrawalToken.Hash"/> is a plain SHA-256 hex
/// digest of its secret: unlike a password verifier, a Withdrawal Token is already high-entropy
/// random data rather than a human-chosen value, so it needs no per-token salt or slow KDF to resist
/// offline guessing -- the secret space itself (256 bits) is the defense. Comparison happens by
/// ordinary equality against a value looked up from a unique index, not by scanning every stored
/// hash, so a constant-time compare is not required to avoid a timing side channel.
/// </summary>
public static class CloudWithdrawalTokenHasher
{
    private const int SecretByteLength = 32;

    /// <summary>
    /// Mints a new token: a random 256-bit secret rendered as an unpadded, URL-safe Base64 string
    /// (short enough to comfortably paste into an ACE chat command) plus its SHA-256 hash.
    /// </summary>
    public static CloudWithdrawalToken Generate()
    {
        var secretBytes = RandomNumberGenerator.GetBytes(SecretByteLength);
        var secret = Convert.ToBase64String(secretBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return new CloudWithdrawalToken(secret, Hash(secret));
    }

    /// <summary>
    /// Computes the same one-way verifier <see cref="Generate"/> would have produced, so a
    /// redemption request's typed/pasted secret can be looked up by its hash without ACE ever
    /// persisting the secret itself.
    /// </summary>
    public static string Hash(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("A Withdrawal Token secret is required.", nameof(secret));
        }

        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret.Trim()));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
