namespace ACE.Cloud.Domain;

/// <summary>
/// A validated lowercase hexadecimal SHA-256 digest (64 characters). Upload requests and chunk
/// records carry checksums as this type rather than a raw string so a malformed or wrong-length
/// value is rejected once, at the boundary, instead of every call site re-checking format before
/// comparing (ASSET-002: "wrong format/checksum").
/// </summary>
public readonly record struct CloudAssetChecksum
{
    private const int Sha256HexLength = 64;

    private readonly string? _value;

    private CloudAssetChecksum(string normalized)
    {
        _value = normalized;
    }

    public string Value => _value ?? throw new InvalidOperationException("Use CloudAssetChecksum.TryParse; the default value is not a valid checksum.");

    public static bool TryParse(string? raw, out CloudAssetChecksum checksum)
    {
        checksum = default;

        if (string.IsNullOrEmpty(raw) || raw.Length != Sha256HexLength)
        {
            return false;
        }

        foreach (var c in raw)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        checksum = new CloudAssetChecksum(raw.ToLowerInvariant());
        return true;
    }

    public bool Equals(CloudAssetChecksum other) => string.Equals(_value, other._value, StringComparison.Ordinal);

    public override int GetHashCode() => _value?.GetHashCode(StringComparison.Ordinal) ?? 0;

    public override string ToString() => _value ?? string.Empty;
}
