namespace ACE.Cloud.Domain;

/// <summary>
/// The narrow, player-facing subset of one Cloud Item's properties that
/// <see cref="CloudAppraisalProjector"/> turns into a Full Cloud Appraisal panel (UI-004). This is
/// intentionally never the sole persisted representation of an item's properties: the full raw
/// property set stays available from the native biota for authorized search (SRCH-001) completely
/// independently of this projection contract (Green: "preserve raw properties separately for
/// authorized search without exposing them in appraisal").
///
/// <see cref="ScribeAccountName"/>, <see cref="HouseOwnerAccountName"/>,
/// <see cref="AllowedWielderInstanceId"/>, and <see cref="AllowedActivatorInstanceId"/> mirror ACE's
/// own administrator-only/internal raw properties (<c>PropertyString.ScribeAccount</c>,
/// <c>PropertyString.HouseOwnerAccount</c>, <c>PropertyInstanceId.AllowedWielder</c>/
/// <c>AllowedActivator</c>). They exist on this snapshot -- rather than a separate raw-property type --
/// specifically so a snapshot that legitimately carries them still proves
/// <see cref="CloudAppraisalProjector"/> never surfaces their values into a panel Line: only the
/// allowed-wielder/activator instance IDs' presence (greater than zero) becomes a redacted boolean
/// requirement line, exactly mirroring <c>AppraiseInfo.BuildProfile</c>'s own
/// <c>AppraisalHasAllowedWielder</c>/<c>AppraisalHasAllowedActivator</c> handling (CONTEXT.md: "Full
/// Cloud Appraisal excludes internal administrator-only fields").
/// </summary>
public sealed record CloudAppraisalRawItemSnapshot
{
    public required CloudItemId ItemId { get; init; }

    public required string Name { get; init; }

    public string? LongDescription { get; init; }

    public string? UseDescription { get; init; }

    public int? Value { get; init; }

    public int? Burden { get; init; }

    public int? Workmanship { get; init; }

    public string? MaterialName { get; init; }

    public string? GemName { get; init; }

    public int? GemCount { get; init; }

    public int? Spellcraft { get; init; }

    public int? CastingDifficulty { get; init; }

    public int? ManaCost { get; init; }

    public int? CurrentMana { get; init; }

    public int? MaxMana { get; init; }

    public IReadOnlyList<CloudAppraisalWieldRequirement> WieldRequirements { get; init; } = [];

    /// <summary>Attuned or Sticky (<c>PropertyInt.Attuned</c> 1 or higher): cannot be traded, sold, or given away.</summary>
    public bool IsAttunedOrSticky { get; init; }

    /// <summary>Bonded: destroyed if lost or intentionally dropped.</summary>
    public bool IsBonded { get; init; }

    public CloudAppraisalArmorProfile? ArmorProfile { get; init; }

    public CloudAppraisalWeaponProfile? WeaponProfile { get; init; }

    public IReadOnlyList<CloudAppraisalSpellReference> Spells { get; init; } = [];

    /// <summary>Administrator-only. <see cref="CloudAppraisalProjector"/> never reads this value.</summary>
    public string? ScribeAccountName { get; init; }

    /// <summary>Administrator-only. <see cref="CloudAppraisalProjector"/> never reads this value.</summary>
    public string? HouseOwnerAccountName { get; init; }

    /// <summary>Internal instance ID, never player-facing. Only its presence becomes a redacted requirement line.</summary>
    public uint? AllowedWielderInstanceId { get; init; }

    /// <summary>Internal instance ID, never player-facing. Only its presence becomes a redacted requirement line.</summary>
    public uint? AllowedActivatorInstanceId { get; init; }

    // The compiler-synthesized record equality would compare WieldRequirements/Spells (both
    // IReadOnlyList<T>) by reference, so this type needs a full manual override -- once any member is
    // overridden, every other property must be included here too.
    public bool Equals(CloudAppraisalRawItemSnapshot? other) =>
        other is not null
        && ItemId == other.ItemId
        && Name == other.Name
        && LongDescription == other.LongDescription
        && UseDescription == other.UseDescription
        && Value == other.Value
        && Burden == other.Burden
        && Workmanship == other.Workmanship
        && MaterialName == other.MaterialName
        && GemName == other.GemName
        && GemCount == other.GemCount
        && Spellcraft == other.Spellcraft
        && CastingDifficulty == other.CastingDifficulty
        && ManaCost == other.ManaCost
        && CurrentMana == other.CurrentMana
        && MaxMana == other.MaxMana
        && WieldRequirements.SequenceEqual(other.WieldRequirements)
        && IsAttunedOrSticky == other.IsAttunedOrSticky
        && IsBonded == other.IsBonded
        && ArmorProfile == other.ArmorProfile
        && WeaponProfile == other.WeaponProfile
        && Spells.SequenceEqual(other.Spells)
        && ScribeAccountName == other.ScribeAccountName
        && HouseOwnerAccountName == other.HouseOwnerAccountName
        && AllowedWielderInstanceId == other.AllowedWielderInstanceId
        && AllowedActivatorInstanceId == other.AllowedActivatorInstanceId;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ItemId);
        hash.Add(Name);
        hash.Add(LongDescription);
        hash.Add(UseDescription);
        hash.Add(Value);
        hash.Add(Burden);
        hash.Add(Workmanship);
        hash.Add(MaterialName);
        hash.Add(GemName);
        hash.Add(GemCount);
        hash.Add(Spellcraft);
        hash.Add(CastingDifficulty);
        hash.Add(ManaCost);
        hash.Add(CurrentMana);
        hash.Add(MaxMana);
        foreach (var requirement in WieldRequirements)
        {
            hash.Add(requirement);
        }
        hash.Add(IsAttunedOrSticky);
        hash.Add(IsBonded);
        hash.Add(ArmorProfile);
        hash.Add(WeaponProfile);
        foreach (var spell in Spells)
        {
            hash.Add(spell);
        }
        hash.Add(ScribeAccountName);
        hash.Add(HouseOwnerAccountName);
        hash.Add(AllowedWielderInstanceId);
        hash.Add(AllowedActivatorInstanceId);
        return hash.ToHashCode();
    }
}
