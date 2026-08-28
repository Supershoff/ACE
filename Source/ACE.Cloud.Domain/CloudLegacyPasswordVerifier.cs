using System.Security.Cryptography;
using System.Text;

using ACE.Common.Cryptography;

namespace ACE.Cloud.Domain;

/// <summary>
/// Reimplements the verification half of ACE.Database's
/// <c>AccountExtensions.PasswordMatches</c> (AUTH-002) against a snapshot instead of a live
/// ACE.Database <c>Account</c> entity: the Auth Bridge intentionally never references ACE.Database
/// (no native-biota mutation repositories, no world-object coupling), but reuses the same bcrypt
/// primitive (<see cref="BCryptProvider"/>) and the same legacy SHA-512-salted scheme for accounts
/// not yet migrated. Unlike <c>AccountExtensions.PasswordMatches</c>, this never persists a bcrypt
/// migration -- the bridge's database identity has read-only SELECT access to
/// <c>ace_auth.account</c> and cannot write it; a caller that wants ACE's own migrate-on-verify
/// behavior must still log into the game client, which continues to use the original write path.
/// </summary>
public static class CloudLegacyPasswordVerifier
{
    private const string BCryptMarker = "use bcrypt";

    public static bool Matches(string passwordHash, string passwordSalt, string candidatePassword)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);
        ArgumentNullException.ThrowIfNull(passwordSalt);
        ArgumentNullException.ThrowIfNull(candidatePassword);

        if (passwordSalt == BCryptMarker)
        {
            return BCryptProvider.Verify(candidatePassword, passwordHash);
        }

        string candidateHash;
        try
        {
            candidateHash = ComputeLegacyHash(candidatePassword, passwordSalt);
        }
        catch (FormatException)
        {
            // A corrupted/non-base64 legacy salt can never match; report a mismatch rather than
            // letting the exception escape and turn a bad-data case into an unhandled 500.
            return false;
        }

        return FixedTimeEquals(candidateHash, passwordHash);
    }

    private static string ComputeLegacyHash(string password, string base64Salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var saltBytes = Convert.FromBase64String(base64Salt);
        var buffer = new byte[passwordBytes.Length + saltBytes.Length];
        passwordBytes.CopyTo(buffer, 0);
        saltBytes.CopyTo(buffer, passwordBytes.Length);

        var hash = SHA512.HashData(buffer);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Constant-time comparison (security baseline: "compare safely"), unlike the plain <c>==</c>
    /// ACE.Database's own legacy branch uses -- <see cref="CryptographicOperations.FixedTimeEquals"/>
    /// safely handles unequal lengths by returning false without branching on their content.
    /// </summary>
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
