namespace ACE.Cloud.Domain;

/// <summary>
/// One numeric appraisal stat and its highlight/color state (UI-004's "colors/flags"), mirroring ACE's
/// own <c>ArmorMask</c>/<c>WeaponMask</c>/<c>ResistMask</c> highlight+color bit pairs: a stat is
/// "highlighted" when an active enchantment (including a preserved Frozen Enchantment, DEP-005) is
/// currently modifying its base value, and "buffed" (vs. debuffed) only has meaning when highlighted.
/// The producing side (ACE, which already runs <c>EnchantmentManager</c>) supplies the resolved value
/// and flags directly; this pure projection never recomputes enchantment math.
/// </summary>
public sealed record CloudAppraisalStatValue
{
    public required double Value { get; init; }

    public bool IsHighlighted { get; init; }

    public bool IsBuffed { get; init; }

    public static CloudAppraisalStatValue Plain(double value) => new() { Value = value };
}
