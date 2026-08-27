namespace ACE.Cloud.Domain;

/// <summary>
/// Base for opaque Guid-backed Cloud identifiers (ARCH-001, ARCH-006, EVT-002): every derived
/// identifier rejects an empty value and compares by underlying value, but two different
/// identifier kinds (for example <see cref="CloudAccountId"/> and <see cref="CloudStackLotId"/>)
/// are never comparable or interchangeable at compile time, even though both wrap a Guid.
/// </summary>
public abstract class CloudGuidId<TSelf> : IEquatable<TSelf>
    where TSelf : CloudGuidId<TSelf>
{
    public Guid Value { get; }

    protected CloudGuidId(Guid value, string requirementMessage)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(requirementMessage, nameof(value));
        }

        Value = value;
    }

    public bool Equals(TSelf? other) => other is not null && Value == other.Value;

    public override bool Equals(object? obj) => Equals(obj as TSelf);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => Value.ToString();

    public static bool operator ==(CloudGuidId<TSelf>? left, CloudGuidId<TSelf>? right) =>
        left is null ? right is null : left.Equals(right as TSelf);

    public static bool operator !=(CloudGuidId<TSelf>? left, CloudGuidId<TSelf>? right) => !(left == right);
}
