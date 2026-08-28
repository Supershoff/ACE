namespace ACE.Cloud.Domain;

/// <summary>
/// The stable identity of one Custodian Location, independent of its current resolved position
/// (DEP-008: hot-apply diffing needs to recognize "this is still the same location" across
/// reapplications even though a Mansion's or the Marketplace's exact coordinates are read fresh each
/// time). The Marketplace location is a singleton; a Mansion location is keyed by its stable
/// world-content identity; a Custom location is keyed by its persisted row ID -- editing a custom
/// position is therefore always a remove-then-add of a new key, never an in-place identity change,
/// which is what lets <see cref="CloudCustodianSpawnPlanner"/> despawn the old Custodian instance and
/// spawn a fresh one under the new configuration version rather than silently relocating a live NPC.
/// </summary>
public sealed class CloudCustodianLocationKey : IEquatable<CloudCustodianLocationKey>
{
    public CloudCustodianLocationKind Kind { get; }

    /// <summary>
    /// Null only for <see cref="Marketplace"/>, the one singleton location. A Mansion's identity is
    /// its native ace_world <c>LandblockInstance.Guid</c> (an ACE object GUID, not a System.Guid)
    /// formatted as an 8-digit hex string; a Custom position's identity is its persisted row ID.
    /// Normalized to a plain string here so this pure domain type does not need to know which of the
    /// two identity systems a given location kind uses.
    /// </summary>
    public string? Id { get; }

    private CloudCustodianLocationKey(CloudCustodianLocationKind kind, string? id)
    {
        Kind = kind;
        Id = id;
    }

    public static readonly CloudCustodianLocationKey Marketplace = new(CloudCustodianLocationKind.Marketplace, null);

    public static CloudCustodianLocationKey ForMansion(uint mansionGuid)
    {
        if (mansionGuid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mansionGuid), "A Mansion location key requires a real Mansion object GUID.");
        }

        return new CloudCustodianLocationKey(CloudCustodianLocationKind.Mansion, mansionGuid.ToString("X8"));
    }

    public static CloudCustodianLocationKey ForCustom(Guid customPositionId)
    {
        if (customPositionId == Guid.Empty)
        {
            throw new ArgumentException("A Custom location key requires a real position ID.", nameof(customPositionId));
        }

        return new CloudCustodianLocationKey(CloudCustodianLocationKind.Custom, customPositionId.ToString());
    }

    public bool Equals(CloudCustodianLocationKey? other) => other is not null && Kind == other.Kind && Id == other.Id;

    public override bool Equals(object? obj) => Equals(obj as CloudCustodianLocationKey);

    public override int GetHashCode() => HashCode.Combine(Kind, Id);

    public override string ToString() => Id is null ? Kind.ToString() : $"{Kind}:{Id}";
}
