namespace ACE.Cloud.Domain;

/// <summary>
/// The weapon combat statistics ACE's client shows for melee/missile weapons and casters (mirrors
/// <c>WeaponProfile</c>/<c>WeaponMask</c>/<c>ResistMask</c>'s mana-conversion and elemental-damage
/// stats). <see cref="ManaConversionModifier"/> and <see cref="ElementalDamageModifier"/> only apply
/// to casters and are omitted for melee/missile weapons.
/// </summary>
public sealed record CloudAppraisalWeaponProfile
{
    public required CloudAppraisalWeaponDamageType DamageType { get; init; }

    public required CloudAppraisalWeaponSkill Skill { get; init; }

    public required CloudAppraisalStatValue Damage { get; init; }

    public required CloudAppraisalStatValue Speed { get; init; }

    public required CloudAppraisalStatValue DamageVariance { get; init; }

    public required CloudAppraisalStatValue DamageModifier { get; init; }

    public required CloudAppraisalStatValue AttackSkillBonus { get; init; }

    public required CloudAppraisalStatValue DefenseBonus { get; init; }

    public CloudAppraisalStatValue? ManaConversionModifier { get; init; }

    public CloudAppraisalStatValue? ElementalDamageModifier { get; init; }
}
