using System.Globalization;

namespace ACE.Cloud.Domain;

/// <summary>
/// A parsed ACE position string in ACE's own LOC format, "0xLLLLLLLL [x y z] qw qx qy qz" (DEP-007:
/// "custom full ACE position strings"), kept independent of ACE.Entity.Position so Custodian
/// Location validation and hot-apply diffing can run as pure, database- and world-free logic.
/// ACE.Server converts this into a real Position only at the moment it actually spawns or moves a
/// Cloud Custodian. Deliberately mirrors the exact tokenizing rules ACE's own `@teleloc` admin
/// command and `Position.ToLOCString()` already use, so a position an administrator copies from
/// `@loc` output round-trips without a second, incompatible parser.
/// </summary>
public sealed class CloudCustodianPosition : IEquatable<CloudCustodianPosition>
{
    public string Raw { get; }

    public uint Landblock { get; }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    public float RotationW { get; }

    public float RotationX { get; }

    public float RotationY { get; }

    public float RotationZ { get; }

    private CloudCustodianPosition(
        string raw, uint landblock, float x, float y, float z, float rotationW, float rotationX, float rotationY, float rotationZ)
    {
        Raw = raw;
        Landblock = landblock;
        X = x;
        Y = y;
        Z = z;
        RotationW = rotationW;
        RotationX = rotationX;
        RotationY = rotationY;
        RotationZ = rotationZ;
    }

    /// <summary>
    /// Parses <paramref name="raw"/>, or returns null when it is not a well-formed ACE position
    /// string (DEP-007's "invalid positions" Red test) -- for example a missing landblock, a
    /// non-hex landblock token, or fewer/more than the expected 7 coordinate/rotation numbers.
    /// </summary>
    public static CloudCustodianPosition? TryParse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var tokens = raw.Replace('[', ' ').Replace(']', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length != 8)
        {
            return null;
        }

        var landblockToken = tokens[0];
        if (landblockToken.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            landblockToken = landblockToken[2..];
        }

        if (!uint.TryParse(landblockToken, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var landblock))
        {
            return null;
        }

        var floats = new float[7];
        for (var i = 0; i < 7; i++)
        {
            if (!float.TryParse(tokens[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out floats[i]))
            {
                return null;
            }
        }

        return new CloudCustodianPosition(raw.Trim(), landblock, floats[0], floats[1], floats[2], floats[3], floats[4], floats[5], floats[6]);
    }

    /// <summary>
    /// Positional identity used for duplicate-location detection (DEP-007's "duplicate positions"
    /// Red test): two position strings that resolve to the exact same landblock and coordinates are
    /// duplicates of each other even when their raw text differs (whitespace, precision, a leading
    /// "0x"), and this equality intentionally ignores rotation -- two Custodians cannot usefully
    /// occupy the same point facing different directions.
    /// </summary>
    public bool Equals(CloudCustodianPosition? other) =>
        other is not null && Landblock == other.Landblock && X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => Equals(obj as CloudCustodianPosition);

    public override int GetHashCode() => HashCode.Combine(Landblock, X, Y, Z);

    public override string ToString() => Raw;
}
