namespace ACE.Cloud.Domain;

/// <summary>
/// One ordered wield-requirement slot (ACE supports up to four independent
/// <c>WieldRequirements</c>/<c>WieldRequirements2..4</c> slots on a single item, e.g. a quest item
/// that requires both a minimum level and a minimum skill). <see cref="CloudAppraisalProjector"/>
/// renders these in the exact order supplied, matching the fixed slot order ACE's client uses.
/// </summary>
public sealed record CloudAppraisalWieldRequirement
{
    public required CloudAppraisalWieldRequirementKind Kind { get; init; }

    /// <summary>The skill or attribute display name. Always null when <see cref="Kind"/> is <see cref="CloudAppraisalWieldRequirementKind.CharacterLevel"/>.</summary>
    public string? SkillOrAttributeName { get; init; }

    public required int MinimumValue { get; init; }
}
