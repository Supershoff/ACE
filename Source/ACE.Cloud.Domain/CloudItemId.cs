using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The exclusive native ACE biota GUID a Cloud Custody Record keeps out of world possession
/// (INV-001). Wraps the same <c>uint</c> GUID space ACE allocates; only ACE's World Boundary
/// Authority may allocate a new value (ARCH-002).
/// </summary>
[JsonConverter(typeof(CloudItemIdJsonConverter))]
public sealed class CloudItemId : IEquatable<CloudItemId>
{
    public uint Value { get; }

    public CloudItemId(uint value)
    {
        if (value == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A Cloud Item ID requires a real native biota GUID.");
        }

        Value = value;
    }

    public bool Equals(CloudItemId? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => Equals(obj as CloudItemId);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();

    public static bool operator ==(CloudItemId? left, CloudItemId? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(CloudItemId? left, CloudItemId? right) => !(left == right);
}
