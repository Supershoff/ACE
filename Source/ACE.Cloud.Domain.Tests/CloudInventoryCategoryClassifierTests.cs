using ACE.Entity.Enum;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #30's Red requirement: "Write table/property tests for ItemType flag combinations,
/// deterministic priority, WeenieType fallback, unknown types, raw-property preservation, stable
/// identity tie-breaks, filters, and sorts" -- the ItemType/WeenieType classification half (UI-001).
/// </summary>
[TestClass]
public sealed class CloudInventoryCategoryClassifierTests
{
    [TestMethod]
    [DataRow(ItemType.MeleeWeapon, CloudInventoryCategory.MeleeWeapons)]
    [DataRow(ItemType.MissileWeapon, CloudInventoryCategory.MissileWeapons)]
    [DataRow(ItemType.Caster, CloudInventoryCategory.Casters)]
    [DataRow(ItemType.Armor, CloudInventoryCategory.Armor)]
    [DataRow(ItemType.Clothing, CloudInventoryCategory.Clothing)]
    [DataRow(ItemType.Jewelry, CloudInventoryCategory.Jewelry)]
    [DataRow(ItemType.Food, CloudInventoryCategory.Foodstuffs)]
    [DataRow(ItemType.Money, CloudInventoryCategory.Currency)]
    [DataRow(ItemType.Gem, CloudInventoryCategory.Gems)]
    [DataRow(ItemType.SpellComponents, CloudInventoryCategory.SpellComponents)]
    [DataRow(ItemType.Writable, CloudInventoryCategory.WrittenMaterial)]
    [DataRow(ItemType.Key, CloudInventoryCategory.Keys)]
    [DataRow(ItemType.Portal, CloudInventoryCategory.Portals)]
    [DataRow(ItemType.ManaStone, CloudInventoryCategory.ManaStones)]
    [DataRow(ItemType.PromissoryNote, CloudInventoryCategory.PromissoryNotes)]
    [DataRow(ItemType.LifeStone, CloudInventoryCategory.LifeStones)]
    [DataRow(ItemType.CraftCookingBase, CloudInventoryCategory.CraftingMaterials)]
    [DataRow(ItemType.CraftAlchemyBase, CloudInventoryCategory.CraftingMaterials)]
    [DataRow(ItemType.CraftFletchingBase, CloudInventoryCategory.CraftingMaterials)]
    [DataRow(ItemType.CraftAlchemyIntermediate, CloudInventoryCategory.CraftingMaterials)]
    [DataRow(ItemType.CraftFletchingIntermediate, CloudInventoryCategory.CraftingMaterials)]
    [DataRow(ItemType.TinkeringTool, CloudInventoryCategory.CraftingMaterials)]
    [DataRow(ItemType.TinkeringMaterial, CloudInventoryCategory.CraftingMaterials)]
    public void Classify_SingleItemTypeFlag_MapsToItsDocumentedCategory(ItemType itemType, CloudInventoryCategory expected)
    {
        Assert.AreEqual(expected, CloudInventoryCategoryClassifier.Classify(itemType, WeenieType.Undef));
    }

    [TestMethod]
    [DataRow(ItemType.Misc)]
    [DataRow(ItemType.Useless)]
    [DataRow(ItemType.Container)]
    [DataRow(ItemType.Lockable)]
    [DataRow(ItemType.Service)]
    [DataRow(ItemType.MagicWieldable)]
    [DataRow(ItemType.Gameboard)]
    [DataRow(ItemType.Creature)]
    [DataRow(ItemType.None)]
    public void Classify_UnrecognizedItemTypeAndUnrecognizedWeenieType_FallsBackToMiscellaneous(ItemType itemType)
    {
        Assert.AreEqual(CloudInventoryCategory.Miscellaneous, CloudInventoryCategoryClassifier.Classify(itemType, WeenieType.Generic));
    }

    [TestMethod]
    [DataRow(WeenieType.MeleeWeapon, CloudInventoryCategory.MeleeWeapons)]
    [DataRow(WeenieType.MissileLauncher, CloudInventoryCategory.MissileWeapons)]
    [DataRow(WeenieType.Missile, CloudInventoryCategory.MissileWeapons)]
    [DataRow(WeenieType.Ammunition, CloudInventoryCategory.MissileWeapons)]
    [DataRow(WeenieType.Caster, CloudInventoryCategory.Casters)]
    [DataRow(WeenieType.Clothing, CloudInventoryCategory.Clothing)]
    [DataRow(WeenieType.Coin, CloudInventoryCategory.Currency)]
    [DataRow(WeenieType.Food, CloudInventoryCategory.Foodstuffs)]
    [DataRow(WeenieType.Healer, CloudInventoryCategory.Foodstuffs)]
    [DataRow(WeenieType.Gem, CloudInventoryCategory.Gems)]
    [DataRow(WeenieType.SpellComponent, CloudInventoryCategory.SpellComponents)]
    [DataRow(WeenieType.Book, CloudInventoryCategory.WrittenMaterial)]
    [DataRow(WeenieType.Scroll, CloudInventoryCategory.WrittenMaterial)]
    [DataRow(WeenieType.Key, CloudInventoryCategory.Keys)]
    [DataRow(WeenieType.Lockpick, CloudInventoryCategory.Keys)]
    [DataRow(WeenieType.Portal, CloudInventoryCategory.Portals)]
    [DataRow(WeenieType.ManaStone, CloudInventoryCategory.ManaStones)]
    [DataRow(WeenieType.LifeStone, CloudInventoryCategory.LifeStones)]
    [DataRow(WeenieType.CraftTool, CloudInventoryCategory.CraftingMaterials)]
    public void Classify_ItemTypeNone_FallsBackToWeenieType(WeenieType weenieType, CloudInventoryCategory expected)
    {
        Assert.AreEqual(expected, CloudInventoryCategoryClassifier.Classify(ItemType.None, weenieType));
    }

    [TestMethod]
    public void Classify_UnrecognizedWeenieType_FallsBackToMiscellaneous()
    {
        Assert.AreEqual(CloudInventoryCategory.Miscellaneous, CloudInventoryCategoryClassifier.Classify(ItemType.None, WeenieType.Vendor));
    }

    [TestMethod]
    public void Classify_MultipleItemTypeFlags_DeterministicPriorityPicksTheHigherPriorityFlag()
    {
        // MeleeWeapon outranks Armor in the documented priority order, regardless of which bit
        // combination an item happens to carry or the order the flags were combined in.
        Assert.AreEqual(
            CloudInventoryCategory.MeleeWeapons,
            CloudInventoryCategoryClassifier.Classify(ItemType.MeleeWeapon | ItemType.Armor, WeenieType.Undef));
        Assert.AreEqual(
            CloudInventoryCategory.MeleeWeapons,
            CloudInventoryCategoryClassifier.Classify(ItemType.Armor | ItemType.MeleeWeapon, WeenieType.Undef));
    }

    [TestMethod]
    public void Classify_VestementsComposite_PrefersArmorOverClothing()
    {
        Assert.AreEqual(
            CloudInventoryCategory.Armor, CloudInventoryCategoryClassifier.Classify(ItemType.Vestements, WeenieType.Undef));
    }

    [TestMethod]
    public void Classify_ItemTypeFlagPresent_IgnoresWeenieTypeFallback()
    {
        // A well-formed item with both an ItemType flag and an unrelated WeenieType still uses the
        // ItemType priority list; WeenieType is consulted only when no ItemType flag matched.
        Assert.AreEqual(
            CloudInventoryCategory.MeleeWeapons,
            CloudInventoryCategoryClassifier.Classify(ItemType.MeleeWeapon, WeenieType.Coin));
    }

    [TestMethod]
    public void Classify_IsTotal_NeverThrowsForAnyItemTypeBitPattern()
    {
        // Property test: every possible 32-bit ItemType flag combination, including bits with no
        // named ACE value at all, must still resolve to exactly one category without throwing.
        var random = new Random(Seed: 42);
        for (var i = 0; i < 2000; i++)
        {
            var bits = unchecked((uint)random.Next(int.MinValue, int.MaxValue));
            var category = CloudInventoryCategoryClassifier.Classify((ItemType)bits, WeenieType.Generic);
            Assert.IsTrue(Enum.IsDefined(category));
        }
    }
}
