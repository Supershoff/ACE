using System.Text.Json.Serialization;

namespace ACE.Cloud.Domain;

/// <summary>
/// The immutable server ID that identifies exactly one Cloud Shard (ARCH-001). A Cloud Mule
/// deployment binds to one Cloud Shard ID for its lifetime; it is never blank and never
/// reassigned after a deployment is bound.
/// </summary>
[JsonConverter(typeof(CloudShardIdJsonConverter))]
public sealed class CloudShardId : IEquatable<CloudShardId>
{
    public string Value { get; }

    public CloudShardId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A Cloud Shard ID is required and cannot be empty.", nameof(value));
        }

        Value = value.Trim();
    }

    public bool Equals(CloudShardId? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as CloudShardId);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(CloudShardId? left, CloudShardId? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(CloudShardId? left, CloudShardId? right) => !(left == right);
}
