namespace ACE.Cloud.Domain;

/// <summary>
/// The actor identity a Cloud command or Activity Ledger event carries (EVT-002: "immutable
/// actor/owner IDs, display snapshots"). Every actor except <see cref="CloudActorKind.System"/>
/// requires a non-empty identity ID; the display snapshot is always required and immutable.
/// </summary>
public sealed class CloudActorIdentity : IEquatable<CloudActorIdentity>
{
    public CloudActorKind Kind { get; }

    /// <summary>
    /// The account or character ID behind this actor; null only for <see cref="CloudActorKind.System"/>.
    /// </summary>
    public Guid? Id { get; }

    /// <summary>
    /// An immutable display-name snapshot captured at the time of the action (AUTH-003, EVT-002),
    /// so a later character rename or deletion cannot rewrite history.
    /// </summary>
    public string DisplaySnapshot { get; }

    public CloudActorIdentity(CloudActorKind kind, Guid? id, string displaySnapshot)
    {
        if (kind != CloudActorKind.System && (id is null || id == Guid.Empty))
        {
            throw new ArgumentException("A non-System actor identity requires a non-empty ID.", nameof(id));
        }

        if (kind == CloudActorKind.System && id is { } systemId && systemId != Guid.Empty)
        {
            throw new ArgumentException("A System actor identity cannot carry an individual ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displaySnapshot))
        {
            throw new ArgumentException("An actor identity requires a display snapshot.", nameof(displaySnapshot));
        }

        Kind = kind;
        Id = id;
        DisplaySnapshot = displaySnapshot.Trim();
    }

    /// <summary>
    /// The automated-system actor identity, carrying no individual account or character ID.
    /// </summary>
    public static CloudActorIdentity SystemActor(string displaySnapshot) =>
        new(CloudActorKind.System, id: null, displaySnapshot);

    public bool Equals(CloudActorIdentity? other) =>
        other is not null
        && Kind == other.Kind
        && Id == other.Id
        && string.Equals(DisplaySnapshot, other.DisplaySnapshot, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as CloudActorIdentity);

    public override int GetHashCode() => HashCode.Combine(Kind, Id, DisplaySnapshot);

    public static bool operator ==(CloudActorIdentity? left, CloudActorIdentity? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(CloudActorIdentity? left, CloudActorIdentity? right) => !(left == right);
}
