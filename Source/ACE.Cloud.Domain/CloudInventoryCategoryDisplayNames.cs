namespace ACE.Cloud.Domain;

/// <summary>The player-facing display text for one <see cref="CloudInventoryCategory"/>, used in Mule Page names (UI-002).</summary>
public static class CloudInventoryCategoryDisplayNames
{
    private static readonly Dictionary<CloudInventoryCategory, string> DisplayNames = new()
    {
        [CloudInventoryCategory.MeleeWeapons] = "Melee Weapons",
        [CloudInventoryCategory.MissileWeapons] = "Missile Weapons",
        [CloudInventoryCategory.Casters] = "Casters",
        [CloudInventoryCategory.Armor] = "Armor",
        [CloudInventoryCategory.Clothing] = "Clothing",
        [CloudInventoryCategory.Jewelry] = "Jewelry",
        [CloudInventoryCategory.Foodstuffs] = "Foodstuffs",
        [CloudInventoryCategory.Currency] = "Currency",
        [CloudInventoryCategory.Gems] = "Gems",
        [CloudInventoryCategory.SpellComponents] = "Spell Components",
        [CloudInventoryCategory.WrittenMaterial] = "Written Material",
        [CloudInventoryCategory.Keys] = "Keys",
        [CloudInventoryCategory.Portals] = "Portals",
        [CloudInventoryCategory.ManaStones] = "Mana Stones",
        [CloudInventoryCategory.PromissoryNotes] = "Promissory Notes",
        [CloudInventoryCategory.LifeStones] = "Life Stones",
        [CloudInventoryCategory.CraftingMaterials] = "Crafting Materials",
        [CloudInventoryCategory.Miscellaneous] = "Miscellaneous",
    };

    public static string GetDisplayName(CloudInventoryCategory category)
    {
        if (!DisplayNames.TryGetValue(category, out var displayName))
        {
            throw new ArgumentOutOfRangeException(nameof(category), category, "Unrecognized Inventory Category.");
        }

        return displayName;
    }
}
