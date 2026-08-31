using System;
using System.Collections.Generic;

using ACE.Cloud.Domain;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Entity;
using ACE.Server.Network.Structure;

namespace ACE.Server.WorldObjects
{
    /// <summary>
    /// Maps ACE's own native <see cref="AppraiseInfo"/> -- the same complete, non-skill-gated profile
    /// <c>Player.HandleActionIdentifyObject</c> already builds for a successful ID -- into a
    /// <see cref="CloudAppraisalRawItemSnapshot"/> (issue #34 human-acceptance correction: "Capture
    /// the complete rebuildable, player-facing appraisal snapshot ... at the ACE world boundary while
    /// the live WorldObject exists"). Kept as pure, WorldObject-free mapping over an already-built
    /// <see cref="AppraiseInfo"/> so it can run in a unit test (AC Cloud Mule review pattern: see
    /// <see cref="Player.BuildRuntimeEnchantments"/>).
    ///
    /// Deliberately does not attempt per-stat enchantment highlight/buff coloring
    /// (<see cref="AppraiseInfo.ArmorHighlight"/>/<see cref="AppraiseInfo.WeaponHighlight"/>): every
    /// numeric value below is complete and correct, but every <see cref="CloudAppraisalStatValue"/>
    /// is reported with <c>IsHighlighted=false</c> -- a follow-up can wire that purely cosmetic
    /// "currently buffed" indicator once this correction's larger, higher-value gaps (icons, rich
    /// appraisal content, the vendor lockup) are in.
    /// </summary>
    partial class Player
    {
        private const uint AppraisalEnchantmentSpellIdMask = 0x80000000;

        internal static CloudAppraisalRawItemSnapshot BuildAppraisalSnapshot(CloudItemId itemId, string name, AppraiseInfo appraisal)
        {
            ArgumentNullException.ThrowIfNull(itemId);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(appraisal);

            var propertiesInt = appraisal.PropertiesInt ?? new Dictionary<PropertyInt, int>();
            var propertiesString = appraisal.PropertiesString ?? new Dictionary<PropertyString, string>();
            var propertiesBool = appraisal.PropertiesBool ?? new Dictionary<PropertyBool, bool>();

            int? GetInt(PropertyInt key) => propertiesInt.TryGetValue(key, out var value) ? value : null;
            string? GetString(PropertyString key) => propertiesString.TryGetValue(key, out var value) ? value : null;
            bool GetBool(PropertyBool key) => propertiesBool.TryGetValue(key, out var value) && value;

            var materialType = GetInt(PropertyInt.MaterialType);
            var gemType = GetInt(PropertyInt.GemType);
            var attuned = (AttunedStatus)(GetInt(PropertyInt.Attuned) ?? (int)AttunedStatus.Normal);
            var bonded = (BondedStatus)(GetInt(PropertyInt.Bonded) ?? (int)BondedStatus.Normal);

            return new CloudAppraisalRawItemSnapshot
            {
                ItemId = itemId,
                Name = name,
                LongDescription = GetString(PropertyString.LongDesc),
                UseDescription = GetString(PropertyString.Use),
                Value = GetInt(PropertyInt.Value),
                Burden = GetInt(PropertyInt.EncumbranceVal),
                Workmanship = GetInt(PropertyInt.ItemWorkmanship),
                MaterialName = materialType.HasValue ? ((MaterialType)materialType.Value).ToString() : null,
                GemName = gemType.HasValue ? ((MaterialType)gemType.Value).ToString() : null,
                GemCount = GetInt(PropertyInt.GemCount),
                Spellcraft = GetInt(PropertyInt.ItemSpellcraft),
                CastingDifficulty = GetInt(PropertyInt.ItemDifficulty),
                ManaCost = GetInt(PropertyInt.ItemManaCost),
                CurrentMana = GetInt(PropertyInt.ItemCurMana),
                MaxMana = GetInt(PropertyInt.ItemMaxMana),
                WieldRequirements = BuildWieldRequirements(propertiesInt),
                IsAttunedOrSticky = attuned >= AttunedStatus.Attuned,
                IsBonded = bonded == BondedStatus.Bonded,
                ArmorProfile = appraisal.ArmorProfile is { } armor ? MapArmorProfile(armor, GetInt(PropertyInt.ArmorLevel)) : null,
                WeaponProfile = appraisal.WeaponProfile is { } weapon ? MapWeaponProfile(weapon) : null,
                Spells = MapSpells(appraisal.SpellBook),
                ScribeAccountName = GetString(PropertyString.ScribeAccount),
                HouseOwnerAccountName = GetString(PropertyString.HouseOwnerAccount),
                // The raw instance IDs are never available here (AppraiseInfo itself never exposes
                // them, only these two presence booleans -- see AppraiseInfo.BuildProfile), and
                // CloudAppraisalProjector only ever renders their *presence*, never their value, so a
                // fixed non-zero sentinel satisfies the "greater than zero" presence check without
                // this snapshot ever holding a real instance ID for either field.
                AllowedWielderInstanceId = GetBool(PropertyBool.AppraisalHasAllowedWielder) ? uint.MaxValue : null,
                AllowedActivatorInstanceId = GetBool(PropertyBool.AppraisalHasAllowedActivator) ? uint.MaxValue : null,
            };
        }

        private static CloudAppraisalArmorProfile MapArmorProfile(ACE.Server.Network.Structure.ArmorProfile armor, int? armorLevel)
        {
            static CloudAppraisalStatValue Stat(double value) => CloudAppraisalStatValue.Plain(value);

            return new CloudAppraisalArmorProfile
            {
                ArmorLevel = Stat(armorLevel ?? 0),
                Slashing = Stat(armor.SlashingProtection),
                Piercing = Stat(armor.PiercingProtection),
                Bludgeoning = Stat(armor.BludgeoningProtection),
                Cold = Stat(armor.ColdProtection),
                Fire = Stat(armor.FireProtection),
                Acid = Stat(armor.AcidProtection),
                Nether = Stat(armor.NetherProtection),
                Lightning = Stat(armor.LightningProtection),
            };
        }

        private static CloudAppraisalWeaponProfile MapWeaponProfile(ACE.Server.Network.Structure.WeaponProfile weapon)
        {
            static CloudAppraisalStatValue Stat(double value) => CloudAppraisalStatValue.Plain(value);

            return new CloudAppraisalWeaponProfile
            {
                DamageType = MapWeaponDamageType(weapon.DamageType),
                Skill = MapWeaponSkill(weapon.WeaponSkill),
                Damage = Stat(weapon.Damage),
                Speed = Stat(weapon.WeaponTime),
                DamageVariance = Stat(weapon.DamageVariance),
                DamageModifier = Stat(weapon.DamageMod),
                AttackSkillBonus = Stat(weapon.WeaponOffense),
                DefenseBonus = Stat(weapon.WeaponDefense),
                // Mana conversion/elemental damage modifiers only apply to casters, and
                // AppraiseInfo.WeaponProfile is always null for a Caster (AppraiseInfo.BuildWeapon),
                // so a weapon actually mapped here never has either -- matching
                // CloudAppraisalWeaponProfile's own doc comment exactly.
                ManaConversionModifier = null,
                ElementalDamageModifier = null,
            };
        }

        private static CloudAppraisalWeaponDamageType MapWeaponDamageType(DamageType damageType) => damageType switch
        {
            DamageType.Slash => CloudAppraisalWeaponDamageType.Slash,
            DamageType.Pierce => CloudAppraisalWeaponDamageType.Pierce,
            DamageType.Bludgeon => CloudAppraisalWeaponDamageType.Bludgeon,
            DamageType.Fire => CloudAppraisalWeaponDamageType.Fire,
            DamageType.Cold => CloudAppraisalWeaponDamageType.Cold,
            DamageType.Acid => CloudAppraisalWeaponDamageType.Acid,
            DamageType.Electric => CloudAppraisalWeaponDamageType.Electric,
            DamageType.Nether => CloudAppraisalWeaponDamageType.Nether,
            _ => CloudAppraisalWeaponDamageType.Undefined,
        };

        private static CloudAppraisalWeaponSkill MapWeaponSkill(Skill skill) => skill switch
        {
            Skill.UnarmedCombat => CloudAppraisalWeaponSkill.Unarmed,
            Skill.HeavyWeapons => CloudAppraisalWeaponSkill.HeavyWeapons,
            Skill.LightWeapons => CloudAppraisalWeaponSkill.LightWeapons,
            Skill.FinesseWeapons => CloudAppraisalWeaponSkill.FinesseWeapons,
            Skill.MissileWeapons => CloudAppraisalWeaponSkill.MissileWeapons,
            Skill.ThrownWeapon => CloudAppraisalWeaponSkill.ThrownWeapons,
            Skill.TwoHandedCombat => CloudAppraisalWeaponSkill.TwoHandedCombat,
            Skill.WarMagic => CloudAppraisalWeaponSkill.WarMagic,
            Skill.LifeMagic => CloudAppraisalWeaponSkill.LifeMagic,
            Skill.VoidMagic => CloudAppraisalWeaponSkill.VoidMagic,
            _ => CloudAppraisalWeaponSkill.Unarmed,
        };

        /// <summary>
        /// ACE supports up to four independent wield-requirement slots
        /// (<see cref="PropertyInt.WieldRequirements"/>/<c>WieldRequirements2..4</c>). Only the three
        /// requirement kinds <see cref="CloudAppraisalWieldRequirementKind"/> models (character
        /// level, skill, attribute) are mapped; the rarer <see cref="WieldRequirement.IntStat"/>/
        /// <see cref="WieldRequirement.BoolStat"/>/<see cref="WieldRequirement.CreatureType"/>/
        /// <see cref="WieldRequirement.HeritageType"/> requirements are skipped rather than
        /// mis-categorized into one of those three.
        /// </summary>
        internal static IReadOnlyList<CloudAppraisalWieldRequirement> BuildWieldRequirements(IReadOnlyDictionary<PropertyInt, int> propertiesInt)
        {
            var requirements = new List<CloudAppraisalWieldRequirement>();

            AddWieldRequirement(requirements, propertiesInt, PropertyInt.WieldRequirements, PropertyInt.WieldSkillType, PropertyInt.WieldDifficulty);
            AddWieldRequirement(requirements, propertiesInt, PropertyInt.WieldRequirements2, PropertyInt.WieldSkillType2, PropertyInt.WieldDifficulty2);
            AddWieldRequirement(requirements, propertiesInt, PropertyInt.WieldRequirements3, PropertyInt.WieldSkillType3, PropertyInt.WieldDifficulty3);
            AddWieldRequirement(requirements, propertiesInt, PropertyInt.WieldRequirements4, PropertyInt.WieldSkillType4, PropertyInt.WieldDifficulty4);

            return requirements;
        }

        private static void AddWieldRequirement(
            List<CloudAppraisalWieldRequirement> requirements,
            IReadOnlyDictionary<PropertyInt, int> propertiesInt,
            PropertyInt requirementKey,
            PropertyInt typeKey,
            PropertyInt difficultyKey)
        {
            if (!propertiesInt.TryGetValue(requirementKey, out var requirementValue))
            {
                return;
            }

            var typeValue = propertiesInt.TryGetValue(typeKey, out var t) ? t : 0;
            var difficulty = propertiesInt.TryGetValue(difficultyKey, out var d) ? d : 0;

            switch ((WieldRequirement)requirementValue)
            {
                case WieldRequirement.Level:
                    requirements.Add(new CloudAppraisalWieldRequirement
                    {
                        Kind = CloudAppraisalWieldRequirementKind.CharacterLevel,
                        SkillOrAttributeName = null,
                        MinimumValue = difficulty,
                    });
                    break;

                case WieldRequirement.Skill:
                case WieldRequirement.RawSkill:
                case WieldRequirement.Training:
                    requirements.Add(new CloudAppraisalWieldRequirement
                    {
                        Kind = CloudAppraisalWieldRequirementKind.Skill,
                        SkillOrAttributeName = ((Skill)typeValue).ToSentence(),
                        MinimumValue = difficulty,
                    });
                    break;

                case WieldRequirement.Attrib:
                case WieldRequirement.RawAttrib:
                    requirements.Add(new CloudAppraisalWieldRequirement
                    {
                        Kind = CloudAppraisalWieldRequirementKind.Attribute,
                        SkillOrAttributeName = ((PropertyAttribute)typeValue).GetDescription(),
                        MinimumValue = difficulty,
                    });
                    break;

                case WieldRequirement.SecondaryAttrib:
                case WieldRequirement.RawSecondaryAttrib:
                    requirements.Add(new CloudAppraisalWieldRequirement
                    {
                        Kind = CloudAppraisalWieldRequirementKind.Attribute,
                        SkillOrAttributeName = ((PropertyAttribute2nd)typeValue).GetDescription(),
                        MinimumValue = difficulty,
                    });
                    break;
            }
        }

        /// <summary>
        /// Splits one raw <see cref="AppraiseInfo.SpellBook"/> entry into its plain spell ID and
        /// whether the high bit (<see cref="AppraisalEnchantmentSpellIdMask"/>, mirroring
        /// <c>AppraiseInfo</c>'s own private <c>EnchantmentMask</c>) tags it as a currently-active item
        /// enchantment, including a preserved Frozen Enchantment (DEP-005). Kept as its own pure
        /// function, separate from <see cref="MapSpells"/>'s portal.dat/ace_world-dependent name
        /// resolution, so the bit-masking rule itself can run in a unit test.
        /// </summary>
        internal static (uint SpellId, bool IsActiveEnchantment) DecodeAppraisalSpellId(uint rawSpellId) =>
            (rawSpellId & ~AppraisalEnchantmentSpellIdMask, (rawSpellId & AppraisalEnchantmentSpellIdMask) != 0);

        /// <summary>
        /// Resolves each <see cref="AppraiseInfo.SpellBook"/> entry's display name from portal.dat's
        /// spell table (<see cref="Spell"/>, the same resolver ACE's own spell code uses), skipping
        /// any spell ID that cannot be resolved rather than surfacing a placeholder name.
        /// </summary>
        internal static IReadOnlyList<CloudAppraisalSpellReference> MapSpells(IReadOnlyList<uint>? spellBook) =>
            MapSpells(
                spellBook,
                (spellId, ex) => cloudCustodianLog.Warn($"[CLOUD CUSTODIAN] Failed to resolve spell {spellId} for an appraisal snapshot.", ex));

        /// <summary>
        /// <see cref="MapSpells(IReadOnlyList{uint})"/>'s pure resolution loop, with the unresolvable-spell
        /// report taken as a parameter instead of going through <c>cloudCustodianLog</c> directly (AC
        /// Cloud Mule review pattern: see <see cref="TryRunSynchronousPersist"/>) so the "skip rather
        /// than throw" contract can run in a unit test without first bootstrapping <see cref="Player"/>'s
        /// live world/database-backed static state.
        /// </summary>
        internal static IReadOnlyList<CloudAppraisalSpellReference> MapSpells(IReadOnlyList<uint>? spellBook, Action<uint, Exception> onUnresolvedSpell)
        {
            if (spellBook is null || spellBook.Count == 0)
            {
                return [];
            }

            var spells = new List<CloudAppraisalSpellReference>();

            foreach (var rawSpellId in spellBook)
            {
                var (spellId, isActiveEnchantment) = DecodeAppraisalSpellId(rawSpellId);

                Spell spell;
                try
                {
                    spell = new Spell(spellId);
                }
                catch (Exception ex)
                {
                    onUnresolvedSpell(spellId, ex);
                    continue;
                }

                if (spell.NotFound)
                {
                    continue;
                }

                spells.Add(new CloudAppraisalSpellReference
                {
                    Name = spell.Name,
                    IsActiveEnchantment = isActiveEnchantment,
                    IsHarmful = spell.IsHarmful,
                });
            }

            return spells;
        }
    }
}
