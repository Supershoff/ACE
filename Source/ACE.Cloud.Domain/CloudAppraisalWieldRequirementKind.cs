namespace ACE.Cloud.Domain;

/// <summary>
/// The category of one <see cref="CloudAppraisalWieldRequirement"/> line (UI-004's "requirements"),
/// mirroring the distinct wield-requirement wording ACE's client renders for
/// <c>PropertyInt.WieldRequirements</c>/<c>WieldRequirements2..4</c>. A character-level requirement
/// carries no skill/attribute name; skill and attribute requirements both name the thing the wielder
/// must meet, worded slightly differently by <see cref="CloudAppraisalProjector"/>.
/// </summary>
public enum CloudAppraisalWieldRequirementKind
{
    CharacterLevel,
    Skill,
    Attribute,
}
