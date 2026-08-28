using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ACE.Cloud.Domain;

/// <summary>
/// The wire format shared by <see cref="CloudAuthGrantIssuer"/> and <see cref="CloudAuthGrantValidator"/>:
/// <c>base64url(payload).urlEncode(keyId).base64url(HMAC-SHA256(payload))</c>. Kept internal --
/// callers only ever see the composed token string or a parsed <see cref="CloudAuthGrant"/>, never
/// this encoding.
/// </summary>
internal static class CloudAuthGrantCodec
{
    private const char FieldSeparator = '|';
    private const char PartSeparator = '.';

    public static byte[] EncodePayload(uint accountId, string audience, DateTime issuedAtUtc, DateTime expiresAtUtc, Guid nonce)
    {
        var text = string.Join(
            FieldSeparator,
            accountId.ToString(CultureInfo.InvariantCulture),
            Uri.EscapeDataString(audience),
            issuedAtUtc.ToUniversalTime().Ticks,
            expiresAtUtc.ToUniversalTime().Ticks,
            nonce);

        return Encoding.UTF8.GetBytes(text);
    }

    public static byte[] Sign(byte[] payload, byte[] secret) => HMACSHA256.HashData(secret, payload);

    public static string ComposeToken(byte[] payload, string keyId, byte[] signature) =>
        string.Join(PartSeparator, Base64UrlEncode(payload), Uri.EscapeDataString(keyId), Base64UrlEncode(signature));

    public static bool TryDecompose(string? token, out byte[] payload, out string keyId, out byte[] signature)
    {
        payload = [];
        keyId = string.Empty;
        signature = [];

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split(PartSeparator);
        if (parts.Length != 3)
        {
            return false;
        }

        try
        {
            payload = Base64UrlDecode(parts[0]);
            keyId = Uri.UnescapeDataString(parts[1]);
            signature = Base64UrlDecode(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        return payload.Length > 0 && !string.IsNullOrEmpty(keyId) && signature.Length > 0;
    }

    public static bool TryParsePayload(
        byte[] payload, out uint accountId, out string audience, out DateTime issuedAtUtc, out DateTime expiresAtUtc, out Guid nonce)
    {
        accountId = 0;
        audience = string.Empty;
        issuedAtUtc = default;
        expiresAtUtc = default;
        nonce = Guid.Empty;

        var text = Encoding.UTF8.GetString(payload);
        var fields = text.Split(FieldSeparator);
        if (fields.Length != 5)
        {
            return false;
        }

        if (!uint.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out accountId))
        {
            return false;
        }

        audience = Uri.UnescapeDataString(fields[1]);

        if (!long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var issuedTicks)
            || !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresTicks))
        {
            return false;
        }

        issuedAtUtc = new DateTime(issuedTicks, DateTimeKind.Utc);
        expiresAtUtc = new DateTime(expiresTicks, DateTimeKind.Utc);

        return Guid.TryParse(fields[4], out nonce);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };

        return Convert.FromBase64String(padded);
    }
}
