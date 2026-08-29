namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// The always-on synthetic semantic fixtures by item class required by issue #27's Red section:
/// sections, ordering, wording, colors/flags, spells, requirements, values, and special cases. These
/// are self-consistent fixtures this projector must satisfy on every run; verifying real ACE client
/// fidelity against operator-owned captures is the separate, protected #28 human gate
/// (<see cref="CloudAppraisalGoldenComparisonHarness"/>).
/// </summary>
[TestClass]
public sealed class CloudAppraisalProjectorTests
{
    private static readonly CloudItemId ItemId = new(555444333);

    private static CloudAppraisalRawItemSnapshot MinimalItem() => new()
    {
        ItemId = ItemId,
        Name = "A Rusty Shortsword",
    };

    [TestMethod]
    public void Build_MinimalGenericItem_ProducesOnlyTheHeaderSection()
    {
        var panel = CloudAppraisalProjector.Build(MinimalItem());

        Assert.AreEqual("A Rusty Shortsword", panel.ItemName);
        Assert.AreEqual(CloudAppraisalPanel.CurrentContractVersion, panel.ContractVersion);
        Assert.HasCount(1, panel.Sections);
        Assert.AreEqual(CloudAppraisalSectionKind.Header, panel.Sections[0].Kind);
        Assert.AreEqual("A Rusty Shortsword", panel.Sections[0].Lines[0].Text);
        Assert.AreEqual(CloudAppraisalTextStyle.Title, panel.Sections[0].Lines[0].Style);
    }

    [TestMethod]
    public void Build_ItemClass_Weapon_ProducesSectionsInFixedOrderWithWeaponStatsAndValue()
    {
        var snapshot = MinimalItem() with
        {
            Name = "Ivory War Club",
            LongDescription = "A heavy war club carved from ivory.",
            Value = 2500,
            Burden = 90,
            WeaponProfile = new CloudAppraisalWeaponProfile
            {
                DamageType = CloudAppraisalWeaponDamageType.Bludgeon,
                Skill = CloudAppraisalWeaponSkill.HeavyWeapons,
                Damage = new CloudAppraisalStatValue { Value = 35, IsHighlighted = true, IsBuffed = true },
                Speed = CloudAppraisalStatValue.Plain(45),
                DamageVariance = CloudAppraisalStatValue.Plain(0.2),
                DamageModifier = CloudAppraisalStatValue.Plain(1.0),
                AttackSkillBonus = CloudAppraisalStatValue.Plain(0.0),
                DefenseBonus = new CloudAppraisalStatValue { Value = -0.05, IsHighlighted = true, IsBuffed = false },
            },
        };

        var panel = CloudAppraisalProjector.Build(snapshot);

        CollectionAssert.AreEqual(
            new[]
            {
                CloudAppraisalSectionKind.Header,
                CloudAppraisalSectionKind.Description,
                CloudAppraisalSectionKind.WeaponStatistics,
                CloudAppraisalSectionKind.ValueAndBurden,
            },
            panel.Sections.Select(s => s.Kind).ToArray());

        var weaponSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.WeaponStatistics);
        Assert.Contains("Damage Type: Bludgeon", weaponSection.Lines.Select(l => l.Text).ToArray());
        Assert.Contains("Attack Skill: Heavy Weapons", weaponSection.Lines.Select(l => l.Text).ToArray());

        var damageLine = weaponSection.Lines.Single(l => l.Text.StartsWith("Damage:"));
        Assert.AreEqual("Damage: 35", damageLine.Text);
        Assert.AreEqual(CloudAppraisalTextStyle.Positive, damageLine.Style);

        var defenseLine = weaponSection.Lines.Single(l => l.Text.StartsWith("Defense Bonus:"));
        Assert.AreEqual(CloudAppraisalTextStyle.Negative, defenseLine.Style);

        var valueSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.ValueAndBurden);
        CollectionAssert.AreEqual(
            new[] { "Value: 2500", "Burden: 90" },
            valueSection.Lines.Select(l => l.Text).ToArray());
    }

    [TestMethod]
    public void Build_ItemClass_Armor_ProducesArmorProtectionSectionInFixedStatOrder()
    {
        var stat = CloudAppraisalStatValue.Plain(0.5);
        var snapshot = MinimalItem() with
        {
            Name = "Chainmail Hauberk",
            ArmorProfile = new CloudAppraisalArmorProfile
            {
                ArmorLevel = new CloudAppraisalStatValue { Value = 120, IsHighlighted = true, IsBuffed = true },
                Slashing = stat,
                Piercing = stat,
                Bludgeoning = stat,
                Cold = stat,
                Fire = stat,
                Acid = stat,
                Nether = stat,
                Lightning = stat,
            },
        };

        var panel = CloudAppraisalProjector.Build(snapshot);

        var armorSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.ArmorProtection);
        CollectionAssert.AreEqual(
            new[]
            {
                "Armor Level: 120",
                "Slashing Protection: 0.5",
                "Piercing Protection: 0.5",
                "Bludgeoning Protection: 0.5",
                "Cold Protection: 0.5",
                "Fire Protection: 0.5",
                "Acid Protection: 0.5",
                "Nether Protection: 0.5",
                "Lightning Protection: 0.5",
            },
            armorSection.Lines.Select(l => l.Text).ToArray());

        Assert.AreEqual(CloudAppraisalTextStyle.Positive, armorSection.Lines[0].Style);
        Assert.AreEqual(CloudAppraisalTextStyle.Body, armorSection.Lines[1].Style);
    }

    [TestMethod]
    public void Build_ItemClass_Caster_ProducesActivationAndManaConversionLines()
    {
        var snapshot = MinimalItem() with
        {
            Name = "Staff of the Mad King",
            Spellcraft = 250,
            CastingDifficulty = 400,
            ManaCost = 50,
            CurrentMana = 500,
            MaxMana = 500,
            WeaponProfile = new CloudAppraisalWeaponProfile
            {
                DamageType = CloudAppraisalWeaponDamageType.Undefined,
                Skill = CloudAppraisalWeaponSkill.WarMagic,
                Damage = CloudAppraisalStatValue.Plain(0),
                Speed = CloudAppraisalStatValue.Plain(0),
                DamageVariance = CloudAppraisalStatValue.Plain(0),
                DamageModifier = CloudAppraisalStatValue.Plain(1.0),
                AttackSkillBonus = CloudAppraisalStatValue.Plain(0),
                DefenseBonus = CloudAppraisalStatValue.Plain(0),
                ManaConversionModifier = new CloudAppraisalStatValue { Value = 1.25, IsHighlighted = true, IsBuffed = true },
                ElementalDamageModifier = CloudAppraisalStatValue.Plain(0),
            },
        };

        var panel = CloudAppraisalProjector.Build(snapshot);

        var activationSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.Activation);
        CollectionAssert.AreEqual(
            new[] { "Spellcraft: 250", "Difficulty: 400", "Mana Cost: 50", "Mana: 500 / 500" },
            activationSection.Lines.Select(l => l.Text).ToArray());

        var weaponSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.WeaponStatistics);
        var manaConversionLine = weaponSection.Lines.Single(l => l.Text.StartsWith("Mana Conversion Modifier:"));
        Assert.AreEqual(CloudAppraisalTextStyle.Positive, manaConversionLine.Style);
    }

    [TestMethod]
    public void Build_WieldRequirements_RendersEachSlotInOrderWithDistinctWording()
    {
        var snapshot = MinimalItem() with
        {
            WieldRequirements =
            [
                new CloudAppraisalWieldRequirement { Kind = CloudAppraisalWieldRequirementKind.CharacterLevel, MinimumValue = 20 },
                new CloudAppraisalWieldRequirement { Kind = CloudAppraisalWieldRequirementKind.Skill, SkillOrAttributeName = "Heavy Weapons", MinimumValue = 250 },
                new CloudAppraisalWieldRequirement { Kind = CloudAppraisalWieldRequirementKind.Attribute, SkillOrAttributeName = "Strength", MinimumValue = 300 },
            ],
        };

        var panel = CloudAppraisalProjector.Build(snapshot);

        var requirementsSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.Requirements);
        CollectionAssert.AreEqual(
            new[]
            {
                "Wielder must be level 20 or higher.",
                "Wielder must have Heavy Weapons skill of 250 or higher.",
                "Wielder must have Strength of 300 or higher.",
            },
            requirementsSection.Lines.Select(l => l.Text).ToArray());
    }

    [TestMethod]
    public void Build_Spells_InnateAndActiveEnchantmentsRenderWithDistinctColorsUnderAHeaderLine()
    {
        var snapshot = MinimalItem() with
        {
            Spells =
            [
                new CloudAppraisalSpellReference { Name = "Minor Impen Item", IsActiveEnchantment = false },
                new CloudAppraisalSpellReference { Name = "Blood Loather", IsActiveEnchantment = true, IsHarmful = true },
                new CloudAppraisalSpellReference { Name = "Willbender", IsActiveEnchantment = true, IsHarmful = false },
            ],
        };

        var panel = CloudAppraisalProjector.Build(snapshot);

        var spellSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.Spells);
        Assert.AreEqual("Item Enchantments:", spellSection.Lines[0].Text);
        Assert.AreEqual(CloudAppraisalTextStyle.Muted, spellSection.Lines[0].Style);

        Assert.AreEqual(CloudAppraisalTextStyle.Body, spellSection.Lines[1].Style);
        Assert.AreEqual(CloudAppraisalTextStyle.Negative, spellSection.Lines[2].Style);
        Assert.AreEqual(CloudAppraisalTextStyle.Positive, spellSection.Lines[3].Style);
    }

    [TestMethod]
    public void Build_SpecialCases_AttunedAndBondedRenderInSpecialPropertiesSection()
    {
        var snapshot = MinimalItem() with { IsAttunedOrSticky = true, IsBonded = true };

        var panel = CloudAppraisalProjector.Build(snapshot);

        var specialSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.SpecialProperties);
        Assert.HasCount(2, specialSection.Lines);
        Assert.IsTrue(specialSection.Lines.All(l => l.Style == CloudAppraisalTextStyle.Negative));
    }

    [TestMethod]
    public void Build_AllSectionsPresent_AreEmittedInTheFixedRelativeOrder()
    {
        var snapshot = new CloudAppraisalRawItemSnapshot
        {
            ItemId = ItemId,
            Name = "Everything Item",
            LongDescription = "Has a bit of everything.",
            Spellcraft = 100,
            WieldRequirements = [new CloudAppraisalWieldRequirement { Kind = CloudAppraisalWieldRequirementKind.CharacterLevel, MinimumValue = 1 }],
            ArmorProfile = new CloudAppraisalArmorProfile
            {
                ArmorLevel = CloudAppraisalStatValue.Plain(1),
                Slashing = CloudAppraisalStatValue.Plain(1),
                Piercing = CloudAppraisalStatValue.Plain(1),
                Bludgeoning = CloudAppraisalStatValue.Plain(1),
                Cold = CloudAppraisalStatValue.Plain(1),
                Fire = CloudAppraisalStatValue.Plain(1),
                Acid = CloudAppraisalStatValue.Plain(1),
                Nether = CloudAppraisalStatValue.Plain(1),
                Lightning = CloudAppraisalStatValue.Plain(1),
            },
            WeaponProfile = new CloudAppraisalWeaponProfile
            {
                DamageType = CloudAppraisalWeaponDamageType.Slash,
                Skill = CloudAppraisalWeaponSkill.LightWeapons,
                Damage = CloudAppraisalStatValue.Plain(1),
                Speed = CloudAppraisalStatValue.Plain(1),
                DamageVariance = CloudAppraisalStatValue.Plain(1),
                DamageModifier = CloudAppraisalStatValue.Plain(1),
                AttackSkillBonus = CloudAppraisalStatValue.Plain(1),
                DefenseBonus = CloudAppraisalStatValue.Plain(1),
            },
            Spells = [new CloudAppraisalSpellReference { Name = "Some Spell" }],
            Value = 1,
            Burden = 1,
            IsAttunedOrSticky = true,
        };

        var panel = CloudAppraisalProjector.Build(snapshot);

        CollectionAssert.AreEqual(
            new[]
            {
                CloudAppraisalSectionKind.Header,
                CloudAppraisalSectionKind.Description,
                CloudAppraisalSectionKind.Requirements,
                CloudAppraisalSectionKind.Activation,
                CloudAppraisalSectionKind.ArmorProtection,
                CloudAppraisalSectionKind.WeaponStatistics,
                CloudAppraisalSectionKind.Spells,
                CloudAppraisalSectionKind.ValueAndBurden,
                CloudAppraisalSectionKind.SpecialProperties,
            },
            panel.Sections.Select(s => s.Kind).ToArray());
    }

    [TestMethod]
    public void Build_NullSnapshot_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudAppraisalProjector.Build(null!));
    }

    // -- Malformed property combinations (Red: "Test... malformed property combinations") --

    [TestMethod]
    public void Build_NegativeValueAndBurden_AreOmittedRatherThanRenderedAsNonsensical()
    {
        var snapshot = MinimalItem() with { Value = -5, Burden = -1 };

        var panel = CloudAppraisalProjector.Build(snapshot);

        Assert.IsFalse(panel.Sections.Any(s => s.Kind == CloudAppraisalSectionKind.ValueAndBurden));
    }

    [TestMethod]
    public void Build_GemCountWithoutGemName_IsOmitted()
    {
        var snapshot = MinimalItem() with { LongDescription = "Base description.", GemCount = 3, GemName = null };

        var panel = CloudAppraisalProjector.Build(snapshot);

        var descriptionSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.Description);
        Assert.IsFalse(descriptionSection.Lines.Any(l => l.Text.Contains("Adorned")));
    }

    [TestMethod]
    public void Build_GemNameWithoutPositiveGemCount_IsOmitted()
    {
        var snapshot = MinimalItem() with { LongDescription = "Base description.", GemCount = 0, GemName = "Ruby" };

        var panel = CloudAppraisalProjector.Build(snapshot);

        var descriptionSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.Description);
        Assert.IsFalse(descriptionSection.Lines.Any(l => l.Text.Contains("Adorned")));
    }

    [TestMethod]
    public void Build_NonPositiveWieldRequirementMinimum_IsSkipped()
    {
        var snapshot = MinimalItem() with
        {
            WieldRequirements = [new CloudAppraisalWieldRequirement { Kind = CloudAppraisalWieldRequirementKind.CharacterLevel, MinimumValue = 0 }],
        };

        var panel = CloudAppraisalProjector.Build(snapshot);

        Assert.IsFalse(panel.Sections.Any(s => s.Kind == CloudAppraisalSectionKind.Requirements));
    }

    [TestMethod]
    public void Build_SkillRequirementWithoutAName_IsSkipped()
    {
        var snapshot = MinimalItem() with
        {
            WieldRequirements = [new CloudAppraisalWieldRequirement { Kind = CloudAppraisalWieldRequirementKind.Skill, SkillOrAttributeName = null, MinimumValue = 100 }],
        };

        var panel = CloudAppraisalProjector.Build(snapshot);

        Assert.IsFalse(panel.Sections.Any(s => s.Kind == CloudAppraisalSectionKind.Requirements));
    }

    [TestMethod]
    public void Build_SpellWithNoName_IsSkippedRatherThanRenderedAsABlankLine()
    {
        var snapshot = MinimalItem() with
        {
            Spells =
            [
                new CloudAppraisalSpellReference { Name = "   " },
                new CloudAppraisalSpellReference { Name = "Real Spell" },
            ],
        };

        var panel = CloudAppraisalProjector.Build(snapshot);

        var spellSection = panel.Sections.Single(s => s.Kind == CloudAppraisalSectionKind.Spells);
        CollectionAssert.AreEqual(new[] { "Item Enchantments:", "Real Spell" }, spellSection.Lines.Select(l => l.Text).ToArray());
    }
}
