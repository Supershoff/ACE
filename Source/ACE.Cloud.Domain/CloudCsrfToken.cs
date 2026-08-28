using System.Security.Cryptography;
using System.Text;

namespace ACE.Cloud.Domain;

/// <summary>Mints unpredictable CSRF tokens for the double-submit pattern (security baseline: "CSRF protection").</summary>
public static class CloudCsrfTokenGenerator
{
    private const int TokenByteLength = 32;

    public static string Generate() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenByteLength))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

/// <summary>
/// Double-submit CSRF token comparison: the value a caller submitted (header/body) against the
/// value recorded on their session. A constant-time compare avoids leaking how many leading bytes
/// of a guessed token happened to match.
/// </summary>
public static class CloudCsrfTokenValidator
{
    public static bool Matches(string? submittedToken, string? sessionToken)
    {
        if (string.IsNullOrEmpty(submittedToken) || string.IsNullOrEmpty(sessionToken))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(submittedToken), Encoding.UTF8.GetBytes(sessionToken));
    }
}
