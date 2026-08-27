using System.Globalization;

namespace ACE.Cloud.Domain;

/// <summary>
/// A parsed <c>Major.Minor.Patch</c> contract protocol version (OPS-002). Unlike the exact-string
/// comparison <see cref="CloudCompatibilityChecker"/> uses for the ACE extension and Cloud schema
/// versions, the contract protocol version must be orderable so a deployment can declare a
/// <see cref="CloudSupportedProtocolWindow"/> spanning more than one released version.
/// </summary>
public sealed class CloudProtocolVersion : IEquatable<CloudProtocolVersion>, IComparable<CloudProtocolVersion>
{
    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public CloudProtocolVersion(int major, int minor, int patch)
    {
        if (major < 0 || minor < 0 || patch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(major), "A protocol version's components cannot be negative.");
        }

        Major = major;
        Minor = minor;
        Patch = patch;
    }

    public static CloudProtocolVersion Parse(string version)
    {
        if (!TryParse(version, out var parsed))
        {
            throw new FormatException($"'{version}' is not a valid Major.Minor.Patch protocol version.");
        }

        return parsed;
    }

    public static bool TryParse(string? version, out CloudProtocolVersion result)
    {
        result = null!;

        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var parts = version.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return false;
        }

        result = new CloudProtocolVersion(major, minor, patch);
        return true;
    }

    public int CompareTo(CloudProtocolVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var majorComparison = Major.CompareTo(other.Major);
        if (majorComparison != 0)
        {
            return majorComparison;
        }

        var minorComparison = Minor.CompareTo(other.Minor);
        return minorComparison != 0 ? minorComparison : Patch.CompareTo(other.Patch);
    }

    public bool Equals(CloudProtocolVersion? other) =>
        other is not null && Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    public override bool Equals(object? obj) => Equals(obj as CloudProtocolVersion);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    public static bool operator ==(CloudProtocolVersion? left, CloudProtocolVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(CloudProtocolVersion? left, CloudProtocolVersion? right) => !(left == right);

    public static bool operator <(CloudProtocolVersion left, CloudProtocolVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(CloudProtocolVersion left, CloudProtocolVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(CloudProtocolVersion left, CloudProtocolVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(CloudProtocolVersion left, CloudProtocolVersion right) => left.CompareTo(right) >= 0;
}
