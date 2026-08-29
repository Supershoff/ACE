namespace ACE.Cloud.Domain;

/// <summary>The player-facing attack skill shown in the weapon statistics section of a Full Cloud Appraisal.</summary>
public enum CloudAppraisalWeaponSkill
{
    Unarmed,
    HeavyWeapons,
    LightWeapons,
    FinesseWeapons,
    MissileWeapons,
    ThrownWeapons,
    TwoHandedCombat,
    WarMagic,
    LifeMagic,
    VoidMagic,
}

public static class CloudAppraisalWeaponSkillExtensions
{
    /// <summary>The exact spaced label ACE's client uses for this skill (e.g. "Heavy Weapons").</summary>
    public static string ToDisplayName(this CloudAppraisalWeaponSkill skill) => skill switch
    {
        CloudAppraisalWeaponSkill.Unarmed => "Unarmed Combat",
        CloudAppraisalWeaponSkill.HeavyWeapons => "Heavy Weapons",
        CloudAppraisalWeaponSkill.LightWeapons => "Light Weapons",
        CloudAppraisalWeaponSkill.FinesseWeapons => "Finesse Weapons",
        CloudAppraisalWeaponSkill.MissileWeapons => "Missile Weapons",
        CloudAppraisalWeaponSkill.ThrownWeapons => "Thrown Weapons",
        CloudAppraisalWeaponSkill.TwoHandedCombat => "Two Handed Combat",
        CloudAppraisalWeaponSkill.WarMagic => "War Magic",
        CloudAppraisalWeaponSkill.LifeMagic => "Life Magic",
        CloudAppraisalWeaponSkill.VoidMagic => "Void Magic",
        _ => skill.ToString(),
    };
}
