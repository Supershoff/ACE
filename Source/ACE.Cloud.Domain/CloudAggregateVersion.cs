namespace ACE.Cloud.Domain;

/// <summary>
/// The optimistic concurrency version of one mutable Cloud aggregate (ARCH-006, transaction rule
/// 3). A command's <c>ExpectedVersion</c> must match an aggregate's current authoritative version
/// before the boundary transaction may commit; every committed event carries the resulting
/// authoritative version.
/// </summary>
public sealed class CloudAggregateVersion : IEquatable<CloudAggregateVersion>, IComparable<CloudAggregateVersion>
{
    public static CloudAggregateVersion Initial { get; } = new(1);

    public int Value { get; }

    public CloudAggregateVersion(int value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "An aggregate version must be a positive integer.");
        }

        Value = value;
    }

    /// <summary>
    /// The version an aggregate takes on after the next committed mutation.
    /// </summary>
    public CloudAggregateVersion Next() => new(Value + 1);

    public bool Equals(CloudAggregateVersion? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => Equals(obj as CloudAggregateVersion);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();

    public int CompareTo(CloudAggregateVersion? other) => other is null ? 1 : Value.CompareTo(other.Value);

    public static bool operator ==(CloudAggregateVersion? left, CloudAggregateVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(CloudAggregateVersion? left, CloudAggregateVersion? right) => !(left == right);

    public static bool operator <(CloudAggregateVersion left, CloudAggregateVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(CloudAggregateVersion left, CloudAggregateVersion right) => left.CompareTo(right) > 0;
}
