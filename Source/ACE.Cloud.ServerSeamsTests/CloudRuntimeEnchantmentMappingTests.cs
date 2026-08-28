using ACE.Entity.Models;
using ACE.Server.WorldObjects;

namespace ACE.Cloud.ServerSeamsTests;

/// <summary>
/// AC Cloud Mule issue #13, DEP-005: <see cref="Player.BuildRuntimeEnchantments"/> reduces a live
/// item's active enchantment registry to the Frozen Enchantment preservation list a Cloud
/// Custodian deposit must carry forward. Exercised directly against plain
/// <see cref="PropertiesEnchantmentRegistry"/> entries (no live WorldObject/database needed) so
/// the mapping's exclusion rules and remaining-duration arithmetic are covered without requiring
/// ACE's world/database bootstrap.
/// </summary>
[TestClass]
public class CloudRuntimeEnchantmentMappingTests
{
    [TestMethod]
    public void BuildRuntimeEnchantments_AnActiveTimedEnchantment_ProducesItsRemainingDuration()
    {
        var entries = new List<PropertiesEnchantmentRegistry>
        {
            new() { SpellId = 1234, Duration = 120, StartTime = -30 },
        };

        var preserved = Player.BuildRuntimeEnchantments(entries);

        Assert.AreEqual(1, preserved.Count);
        Assert.AreEqual(1234, preserved[0].SpellId);
        Assert.AreEqual(90, preserved[0].RemainingDurationSeconds);
    }

    [TestMethod]
    public void BuildRuntimeEnchantments_APermanentEquipLinkedSpell_IsExcluded()
    {
        // DEP-005: "Permanent built-in spells remain ordinary static properties" -- Duration ==
        // -1 marks an equip-linked enchantment (EnchantmentManager.BuildEntry), never frozen.
        var entries = new List<PropertiesEnchantmentRegistry>
        {
            new() { SpellId = 5678, Duration = -1, StartTime = 0 },
        };

        var preserved = Player.BuildRuntimeEnchantments(entries);

        Assert.AreEqual(0, preserved.Count);
    }

    [TestMethod]
    public void BuildRuntimeEnchantments_ACooldownPseudoEntry_IsExcluded()
    {
        // Mirrors EnchantmentManager.RemoveAllEnchantments' own exclusion: cooldown pseudo-spell
        // IDs are always greater than short.MaxValue (EnchantmentManager.GetCooldownSpellID).
        var entries = new List<PropertiesEnchantmentRegistry>
        {
            new() { SpellId = short.MaxValue + 1, Duration = 60, StartTime = 0 },
        };

        var preserved = Player.BuildRuntimeEnchantments(entries);

        Assert.AreEqual(0, preserved.Count);
    }

    [TestMethod]
    public void BuildRuntimeEnchantments_AnAlreadyExpiredEntry_IsExcluded()
    {
        var entries = new List<PropertiesEnchantmentRegistry>
        {
            new() { SpellId = 42, Duration = 60, StartTime = -60 },
        };

        var preserved = Player.BuildRuntimeEnchantments(entries);

        Assert.AreEqual(0, preserved.Count);
    }

    [TestMethod]
    public void BuildRuntimeEnchantments_NoActiveEnchantments_ReturnsAnEmptyList()
    {
        var preserved = Player.BuildRuntimeEnchantments(new List<PropertiesEnchantmentRegistry>());

        Assert.AreEqual(0, preserved.Count);
    }

    [TestMethod]
    public void BuildRuntimeEnchantments_NullRegistry_ReturnsAnEmptyList()
    {
        var preserved = Player.BuildRuntimeEnchantments(null);

        Assert.AreEqual(0, preserved.Count);
    }
}
