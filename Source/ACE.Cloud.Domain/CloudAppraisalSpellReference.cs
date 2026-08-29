namespace ACE.Cloud.Domain;

/// <summary>
/// One spell entry in the appraisal panel's spell list (mirrors <c>AppraiseInfo.SpellBook</c>, which
/// tags a currently-active item enchantment -- including a preserved Frozen Enchantment, DEP-005 --
/// with its high bit set). The spell's display name is resolved by the caller (portal.dat's spell
/// table, outside this pure projection's scope, the same way Icon Reconstruction's DAT-backed
/// resolution stays outside <c>CloudIconCompositor</c>) rather than by this projector.
/// </summary>
public sealed record CloudAppraisalSpellReference
{
    public required string Name { get; init; }

    /// <summary>True for a currently-active item enchantment (including a Frozen Enchantment); false for an innate/static spell.</summary>
    public bool IsActiveEnchantment { get; init; }

    /// <summary>True for a harmful/debuff spell, shown in a different color than a beneficial one.</summary>
    public bool IsHarmful { get; init; }
}
