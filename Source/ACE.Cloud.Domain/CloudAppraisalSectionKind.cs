namespace ACE.Cloud.Domain;

/// <summary>
/// The fixed relative order <see cref="CloudAppraisalProjector"/> renders sections in (UI-004's
/// "section order"). A section absent from a given item's panel is simply omitted -- the remaining
/// sections keep this same relative order.
/// </summary>
public enum CloudAppraisalSectionKind
{
    Header,
    Description,
    Requirements,
    Activation,
    ArmorProtection,
    WeaponStatistics,
    Spells,
    ValueAndBurden,
    SpecialProperties,
}
