using System.Security.Cryptography;

namespace ACE.Cloud.Domain;

/// <summary>
/// A newly minted Cloud web session credential (AUTH-002): <see cref="Secret"/> is the high-entropy
/// value placed in the HttpOnly session cookie; <see cref="Hash"/> is the one-way verifier persisted
/// instead of the secret itself (security baseline: "store a one-way verifier if practical; compare
/// safely"), the exact same shape <c>CloudWithdrawalTokenHasher</c> already uses for Withdrawal
/// Tokens.
/// </summary>
public sealed record CloudWebSessionSecret(string Secret, string Hash);

/// <summary>
/// Generates cryptographically strong, single-use-verifiable session secrets and their one-way
/// verifiers. See <c>CloudWithdrawalTokenHasher</c>'s doc comment for why a plain SHA-256 digest is
/// sufficient here without a per-secret salt or slow KDF: the secret is already 256 bits of random
/// data, not a human-chosen value.
/// </summary>
public static class CloudWebSessionSecretHasher
{
    private const int SecretByteLength = 32;

    public static CloudWebSessionSecret Generate()
    {
        var secretBytes = RandomNumberGenerator.GetBytes(SecretByteLength);
        var secret = Convert.ToBase64String(secretBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return new CloudWebSessionSecret(secret, Hash(secret));
    }

    public static string Hash(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("A session secret is required.", nameof(secret));
        }

        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret.Trim()));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
