using ACE.Cloud.Domain;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;
using ACE.Server.Network.Structure;
using ACE.Server.WorldObjects;

namespace ACE.Cloud.ServerSeamsTests;

/// <summary>
/// Human-acceptance regression (issue #34): the Full Cloud Appraisal panel showed only name/value/
/// burden because nothing mapped ACE's own <see cref="AppraiseInfo"/> -- the same complete,
/// non-skill-gated profile a successful native ID already builds -- into a
/// <see cref="ACE.Cloud.Domain.CloudAppraisalRawItemSnapshot"/>. Exercised directly against
/// <see cref="Player.BuildAppraisalSnapshot"/> and its pure helpers (no live WorldObject/database
/// needed): <see cref="AppraiseInfo"/>'s property dictionaries are plain public fields, so a test can
/// populate them exactly as <c>AppraiseInfo.BuildProperties</c> would.
/// </summary>
[TestClass]
public sealed class CloudCustodianAppraisalSnapshotTests
{
    private static AppraiseInfo EmptyAppraisal() => new()
    {
        PropertiesInt = new Dictionary<PropertyInt, int>(),
        PropertiesString = new Dictionary<PropertyString, string>(),
        PropertiesBool = new Dictionary<PropertyBool, bool>(),
        SpellBook = [],
    };

    [TestMethod]
    public void BuildAppraisalSnapshot_MapsDescriptiveAndMaterialFields()
    {
        var appraisal = EmptyAppraisal();
        appraisal.PropertiesString[PropertyString.LongDesc] = "A finely crafted blade.";
        appraisal.PropertiesString[PropertyString.Use] = "Wield to attack.";
        appraisal.PropertiesInt[PropertyInt.Value] = 500;
        appraisal.PropertiesInt[PropertyInt.EncumbranceVal] = 20;
        appraisal.PropertiesInt[PropertyInt.ItemWorkmanship] = 8;
        appraisal.PropertiesInt[PropertyInt.MaterialType] = (int)MaterialType.Steel;
        appraisal.PropertiesInt[PropertyInt.GemType] = (int)MaterialType.Ruby;
        appraisal.PropertiesInt[PropertyInt.GemCount] = 3;
        appraisal.PropertiesInt[PropertyInt.ItemSpellcraft] = 250;
        appraisal.PropertiesInt[PropertyInt.ItemDifficulty] = 150;
        appraisal.PropertiesInt[PropertyInt.ItemManaCost] = 50;
        appraisal.PropertiesInt[PropertyInt.ItemCurMana] = 500;
        appraisal.PropertiesInt[PropertyInt.ItemMaxMana] = 500;

        var snapshot = Player.BuildAppraisalSnapshot(new CloudItemId(0x80000123), "Steel Sword", appraisal);

        Assert.AreEqual("Steel Sword", snapshot.Name);
        Assert.AreEqual("A finely crafted blade.", snapshot.LongDescription);
        Assert.AreEqual("Wield to attack.", snapshot.UseDescription);
        Assert.AreEqual(500, snapshot.Value);
        Assert.AreEqual(20, snapshot.Burden);
        Assert.AreEqual(8, snapshot.Workmanship);
        Assert.AreEqual(nameof(MaterialType.Steel), snapshot.MaterialName);
        Assert.AreEqual(nameof(MaterialType.Ruby), snapshot.GemName);
        Assert.AreEqual(3, snapshot.GemCount);
        Assert.AreEqual(250, snapshot.Spellcraft);
        Assert.AreEqual(150, snapshot.CastingDifficulty);
        Assert.AreEqual(50, snapshot.ManaCost);
        Assert.AreEqual(500, snapshot.CurrentMana);
        Assert.AreEqual(500, snapshot.MaxMana);
        Assert.IsNull(snapshot.ArmorProfile);
        Assert.IsNull(snapshot.WeaponProfile);
        Assert.HasCount(0, snapshot.Spells);
    }

    [TestMethod]
    [DataRow((int)AttunedStatus.Normal, false)]
    [DataRow((int)AttunedStatus.Attuned, true)]
    [DataRow((int)AttunedStatus.Sticky, true)]
    public void BuildAppraisalSnapshot_Attuned_MapsToIsAttunedOrSticky(int attunedValue, bool expected)
    {
        var appraisal = EmptyAppraisal();
        appraisal.PropertiesInt[PropertyInt.Attuned] = attunedValue;

        var snapshot = Player.BuildAppraisalSnapshot(new CloudItemId(1), "Item", appraisal);

        Assert.AreEqual(expected, snapshot.IsAttunedOrSticky);
    }

    [TestMethod]
    [DataRow((int)BondedStatus.Normal, false)]
    [DataRow((int)BondedStatus.Bonded, true)]
    [DataRow((int)BondedStatus.Sticky, false)]
    public void BuildAppraisalSnapshot_Bonded_OnlyTrueForBondedStatusBonded(int bondedValue, bool expected)
    {
        var appraisal = EmptyAppraisal();
        appraisal.PropertiesInt[PropertyInt.Bonded] = bondedValue;

        var snapshot = Player.BuildAppraisalSnapshot(new CloudItemId(1), "Item", appraisal);

        Assert.AreEqual(expected, snapshot.IsBonded);
    }

    [TestMethod]
    public void BuildAppraisalSnapshot_AllowedWielderPresence_BecomesRedactedSentinelNeverARealId()
    {
        var appraisal = EmptyAppraisal();
        appraisal.PropertiesBool[PropertyBool.AppraisalHasAllowedWielder] = true;
        appraisal.PropertiesBool[PropertyBool.AppraisalHasAllowedActivator] = false;

        var snapshot = Player.BuildAppraisalSnapshot(new CloudItemId(1), "Item", appraisal);

        Assert.IsNotNull(snapshot.AllowedWielderInstanceId);
        Assert.IsTrue(snapshot.AllowedWielderInstanceId > 0);
        Assert.IsNull(snapshot.AllowedActivatorInstanceId);
    }

    [TestMethod]
    public void BuildAppraisalSnapshot_NoAllowedWielderOrActivatorFlags_BothNull()
    {
        var snapshot = Player.BuildAppraisalSnapshot(new CloudItemId(1), "Item", EmptyAppraisal());

        Assert.IsNull(snapshot.AllowedWielderInstanceId);
        Assert.IsNull(snapshot.AllowedActivatorInstanceId);
    }

    [TestMethod]
    public void BuildWieldRequirements_CharacterLevel_HasNoSkillOrAttributeName()
    {
        var properties = new Dictionary<PropertyInt, int>
        {
            [PropertyInt.WieldRequirements] = (int)WieldRequirement.Level,
            [PropertyInt.WieldDifficulty] = 50,
        };

        var requirements = Player.BuildWieldRequirements(properties);

        Assert.HasCount(1, requirements);
        Assert.AreEqual(CloudAppraisalWieldRequirementKind.CharacterLevel, requirements[0].Kind);
        Assert.IsNull(requirements[0].SkillOrAttributeName);
        Assert.AreEqual(50, requirements[0].MinimumValue);
    }

    [TestMethod]
    public void BuildWieldRequirements_Skill_ResolvesSkillDisplayName()
    {
        var properties = new Dictionary<PropertyInt, int>
        {
            [PropertyInt.WieldRequirements] = (int)WieldRequirement.Skill,
            [PropertyInt.WieldSkillType] = (int)Skill.HeavyWeapons,
            [PropertyInt.WieldDifficulty] = 300,
        };

        var requirements = Player.BuildWieldRequirements(properties);

        Assert.HasCount(1, requirements);
        Assert.AreEqual(CloudAppraisalWieldRequirementKind.Skill, requirements[0].Kind);
        Assert.AreEqual("Heavy Weapons", requirements[0].SkillOrAttributeName);
        Assert.AreEqual(300, requirements[0].MinimumValue);
    }

    [TestMethod]
    public void BuildWieldRequirements_Attribute_ResolvesAttributeDisplayName()
    {
        var properties = new Dictionary<PropertyInt, int>
        {
            [PropertyInt.WieldRequirements] = (int)WieldRequirement.Attrib,
            [PropertyInt.WieldSkillType] = (int)PropertyAttribute.Strength,
            [PropertyInt.WieldDifficulty] = 200,
        };

        var requirements = Player.BuildWieldRequirements(properties);

        Assert.HasCount(1, requirements);
        Assert.AreEqual(CloudAppraisalWieldRequirementKind.Attribute, requirements[0].Kind);
        Assert.AreEqual(200, requirements[0].MinimumValue);
    }

    [TestMethod]
    public void BuildWieldRequirements_AllFourSlots_PreservesSlotOrder()
    {
        var properties = new Dictionary<PropertyInt, int>
        {
            [PropertyInt.WieldRequirements] = (int)WieldRequirement.Level,
            [PropertyInt.WieldDifficulty] = 10,
            [PropertyInt.WieldRequirements2] = (int)WieldRequirement.Skill,
            [PropertyInt.WieldSkillType2] = (int)Skill.MissileWeapons,
            [PropertyInt.WieldDifficulty2] = 200,
            [PropertyInt.WieldRequirements3] = (int)WieldRequirement.Attrib,
            [PropertyInt.WieldSkillType3] = (int)PropertyAttribute.Coordination,
            [PropertyInt.WieldDifficulty3] = 150,
        };

        var requirements = Player.BuildWieldRequirements(properties);

        Assert.HasCount(3, requirements);
        Assert.AreEqual(CloudAppraisalWieldRequirementKind.CharacterLevel, requirements[0].Kind);
        Assert.AreEqual(CloudAppraisalWieldRequirementKind.Skill, requirements[1].Kind);
        Assert.AreEqual(CloudAppraisalWieldRequirementKind.Attribute, requirements[2].Kind);
    }

    [TestMethod]
    public void BuildWieldRequirements_UnmodeledRequirementKind_IsSkippedNotMisclassified()
    {
        var properties = new Dictionary<PropertyInt, int>
        {
            [PropertyInt.WieldRequirements] = (int)WieldRequirement.CreatureType,
            [PropertyInt.WieldDifficulty] = 1,
        };

        var requirements = Player.BuildWieldRequirements(properties);

        Assert.HasCount(0, requirements);
    }

    [TestMethod]
    public void BuildWieldRequirements_NoWieldRequirementsSet_ReturnsEmpty()
    {
        var requirements = Player.BuildWieldRequirements(new Dictionary<PropertyInt, int>());

        Assert.HasCount(0, requirements);
    }

    [TestMethod]
    public void DecodeAppraisalSpellId_HighBitSet_IsActiveEnchantmentAndMasksOffTheBit()
    {
        const uint spellId = 1337;
        var (decodedId, isActiveEnchantment) = Player.DecodeAppraisalSpellId(spellId | 0x80000000);

        Assert.AreEqual(spellId, decodedId);
        Assert.IsTrue(isActiveEnchantment);
    }

    [TestMethod]
    public void DecodeAppraisalSpellId_HighBitClear_IsNotActiveEnchantment()
    {
        const uint spellId = 42;
        var (decodedId, isActiveEnchantment) = Player.DecodeAppraisalSpellId(spellId);

        Assert.AreEqual(spellId, decodedId);
        Assert.IsFalse(isActiveEnchantment);
    }

    [TestMethod]
    public void MapSpells_EmptySpellBook_ReturnsEmpty()
    {
        Assert.HasCount(0, Player.MapSpells([]));
        Assert.HasCount(0, Player.MapSpells(null));
    }

    [TestMethod]
    public void MapSpells_UnresolvableSpellId_IsSkippedRatherThanThrowing()
    {
        // No portal.dat/ace_world is loaded in this unit-test process, so every spell ID is
        // unresolvable here; this proves that never crashes the caller and always degrades to an
        // empty (or partial) result instead -- the same "log and continue" contract as every other
        // per-row Cloud Custodian failure path (see CloudCustodianSynchronousPersistTests). Exercised
        // through the two-argument overload -- the same seam TryRunSynchronousPersist uses -- so the
        // report callback doesn't touch Player's live world/database-backed static state.
        var reported = new List<uint>();

        var spells = Player.MapSpells([1u, 2u | 0x80000000], (spellId, ex) =>
        {
            reported.Add(spellId);
            Assert.IsNotNull(ex);
        });

        Assert.HasCount(0, spells);
        Assert.HasCount(2, reported);
        Assert.Contains(1u, reported);
        Assert.Contains(2u, reported);
    }
}
