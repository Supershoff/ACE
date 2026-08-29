namespace ACE.Cloud.Domain;

/// <summary>
/// The eight per-damage-type protection levels ACE's client shows for armor, clothing, and shields
/// (mirrors <c>ArmorProfile</c>/<c>ArmorMask</c>), in the fixed display order
/// <see cref="CloudAppraisalProjector"/> renders them.
/// </summary>
public sealed record CloudAppraisalArmorProfile
{
    public required CloudAppraisalStatValue ArmorLevel { get; init; }

    public required CloudAppraisalStatValue Slashing { get; init; }

    public required CloudAppraisalStatValue Piercing { get; init; }

    public required CloudAppraisalStatValue Bludgeoning { get; init; }

    public required CloudAppraisalStatValue Cold { get; init; }

    public required CloudAppraisalStatValue Fire { get; init; }

    public required CloudAppraisalStatValue Acid { get; init; }

    public required CloudAppraisalStatValue Nether { get; init; }

    public required CloudAppraisalStatValue Lightning { get; init; }
}
