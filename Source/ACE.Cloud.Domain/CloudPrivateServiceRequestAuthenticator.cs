using System.Security.Cryptography;
using System.Text;

namespace ACE.Cloud.Domain;

/// <summary>
/// Signs/verifies requests exchanged between Cloud Mule's own private-network hosts -- the Cloud
/// backend calling the Auth Bridge's internal grant-issuance and access-level endpoints -- using the
/// same symmetric <see cref="CloudPrivateServiceKeyRing"/> that also signs Auth Bridge grants
/// (security baseline: "Private-service authentication between Cloud backend, Auth Bridge, and ACE
/// boundary endpoints; bind privately and support key rotation. Do not expose these endpoints
/// publicly."). The signed timestamp bounds how long a captured header remains replayable
/// (<paramref name="maxClockSkew"/> in <see cref="Validate"/>); these endpoints are additionally
/// never bound to a public network interface, which is the primary defense -- this header proves the
/// caller holds a current private-service key even on a network where that binding was
/// misconfigured.
/// </summary>
public static class CloudPrivateServiceRequestAuthenticator
{
    public static string Sign(string method, string path, DateTime nowUtc, CloudPrivateServiceKeyRing keyRing)
    {
        ArgumentNullException.ThrowIfNull(keyRing);

        var canonical = Canonicalize(method, path, nowUtc);
        var signature = HMACSHA256.HashData(keyRing.ActiveKey.Secret, canonical);

        return $"{keyRing.ActiveKey.KeyId}:{nowUtc.Ticks}:{Convert.ToBase64String(signature)}";
    }

    public static bool Validate(
        string? headerValue, string method, string path, DateTime nowUtc, TimeSpan maxClockSkew, CloudPrivateServiceKeyRing keyRing)
    {
        ArgumentNullException.ThrowIfNull(keyRing);

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        var parts = headerValue.Split(':', 3);
        if (parts.Length != 3)
        {
            return false;
        }

        if (!keyRing.TryGetKey(parts[0], out var key))
        {
            return false;
        }

        if (!long.TryParse(parts[1], out var ticks))
        {
            return false;
        }

        DateTime signedAtUtc;
        try
        {
            signedAtUtc = new DateTime(ticks, DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        if ((nowUtc - signedAtUtc).Duration() > maxClockSkew)
        {
            return false;
        }

        byte[] providedSignature;
        try
        {
            providedSignature = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        var canonical = Canonicalize(method, path, signedAtUtc);
        var expectedSignature = HMACSHA256.HashData(key.Secret, canonical);

        return CryptographicOperations.FixedTimeEquals(expectedSignature, providedSignature);
    }

    private static byte[] Canonicalize(string method, string path, DateTime timestampUtc) =>
        Encoding.UTF8.GetBytes($"{method.ToUpperInvariant()}\n{path}\n{timestampUtc.Ticks}");
}
