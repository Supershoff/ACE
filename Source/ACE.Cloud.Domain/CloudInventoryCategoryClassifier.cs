using ACE.Entity.Enum;

namespace ACE.Cloud.Domain;

/// <summary>
/// Normalizes one item's raw ACE <see cref="ItemType"/> flags and <see cref="WeenieType"/> into
/// exactly one <see cref="CloudInventoryCategory"/> (UI-001). <see cref="ItemType"/> is a
/// <c>[Flags]</c> enum -- a real item can carry more than one bit -- so <see cref="Classify"/> walks
/// <see cref="ItemTypePriority"/> in a fixed, documented order and returns the category for the
/// first flag present; only when none of those flags are present does it fall back to
/// <see cref="WeenieTypeFallback"/>, and only when neither source matches does it return
/// <see cref="CloudInventoryCategory.Miscellaneous"/>. This function is total (every possible input,
/// including <see cref="ItemType.None"/> and an unrecognized <see cref="WeenieType"/>, maps to
/// exactly one category) and pure (no I/O, no ACE.Server world-object coupling -- ARCH-012), so it
/// can run identically whether classifying a live deposit or rebuilding a projection from scratch.
/// </summary>
public static class CloudInventoryCategoryClassifier
{
    /// <summary>
    /// The documented deterministic ItemType priority order (issue #30 UI-001: "using a documented
    /// deterministic priority"). Ordered from the most specific/most gameplay-relevant equipment
    /// types down to the loosest catch-all flags, so an item that happens to carry both a specific
    /// flag (for example <see cref="ItemType.MeleeWeapon"/>) and a broader one (for example
    /// <see cref="ItemType.MagicWieldable"/>) always lands in the specific category. Composite named
    /// values on <see cref="ItemType"/> itself (<c>Vestements</c>, <c>Weapon</c>, <c>Item</c>, ...)
    /// are deliberately not used here: they are convenience OR-groups for ACE's own comparisons, not
    /// priority entries, and checking against them directly would make an Armor|Clothing item match
    /// two entries at once with no defined winner.
    /// </summary>
    private static readonly (ItemType Flag, CloudInventoryCategory Category)[] ItemTypePriority =
    [
        (ItemType.MeleeWeapon, CloudInventoryCategory.MeleeWeapons),
        (ItemType.MissileWeapon, CloudInventoryCategory.MissileWeapons),
        (ItemType.Caster, CloudInventoryCategory.Casters),
        (ItemType.Armor, CloudInventoryCategory.Armor),
        (ItemType.Clothing, CloudInventoryCategory.Clothing),
        (ItemType.Jewelry, CloudInventoryCategory.Jewelry),
        (ItemType.Food, CloudInventoryCategory.Foodstuffs),
        (ItemType.Money, CloudInventoryCategory.Currency),
        (ItemType.Gem, CloudInventoryCategory.Gems),
        (ItemType.SpellComponents, CloudInventoryCategory.SpellComponents),
        (ItemType.Writable, CloudInventoryCategory.WrittenMaterial),
        (ItemType.Key, CloudInventoryCategory.Keys),
        (ItemType.Portal, CloudInventoryCategory.Portals),
        (ItemType.ManaStone, CloudInventoryCategory.ManaStones),
        (ItemType.PromissoryNote, CloudInventoryCategory.PromissoryNotes),
        (ItemType.LifeStone, CloudInventoryCategory.LifeStones),
        (ItemType.CraftCookingBase, CloudInventoryCategory.CraftingMaterials),
        (ItemType.CraftAlchemyBase, CloudInventoryCategory.CraftingMaterials),
        (ItemType.CraftFletchingBase, CloudInventoryCategory.CraftingMaterials),
        (ItemType.CraftAlchemyIntermediate, CloudInventoryCategory.CraftingMaterials),
        (ItemType.CraftFletchingIntermediate, CloudInventoryCategory.CraftingMaterials),
        (ItemType.TinkeringTool, CloudInventoryCategory.CraftingMaterials),
        (ItemType.TinkeringMaterial, CloudInventoryCategory.CraftingMaterials),
    ];

    /// <summary>
    /// The documented WeenieType fallback (UI-001: "a WeenieType fallback"), consulted only when
    /// <see cref="ItemTypePriority"/> found no matching flag -- for example a legacy or malformed
    /// item persisted with <see cref="ItemType.None"/>. Every entry here mirrors the ItemType
    /// category it would otherwise have produced, so the two sources never disagree for a
    /// well-formed item that happens to carry both a recognizable ItemType flag and its "obvious"
    /// WeenieType.
    /// </summary>
    private static readonly Dictionary<WeenieType, CloudInventoryCategory> WeenieTypeFallback = new()
    {
        [WeenieType.MeleeWeapon] = CloudInventoryCategory.MeleeWeapons,
        [WeenieType.MissileLauncher] = CloudInventoryCategory.MissileWeapons,
        [WeenieType.Missile] = CloudInventoryCategory.MissileWeapons,
        [WeenieType.Ammunition] = CloudInventoryCategory.MissileWeapons,
        [WeenieType.Caster] = CloudInventoryCategory.Casters,
        [WeenieType.Clothing] = CloudInventoryCategory.Clothing,
        [WeenieType.Coin] = CloudInventoryCategory.Currency,
        [WeenieType.Food] = CloudInventoryCategory.Foodstuffs,
        [WeenieType.Healer] = CloudInventoryCategory.Foodstuffs,
        [WeenieType.Gem] = CloudInventoryCategory.Gems,
        [WeenieType.SpellComponent] = CloudInventoryCategory.SpellComponents,
        [WeenieType.Book] = CloudInventoryCategory.WrittenMaterial,
        [WeenieType.Scroll] = CloudInventoryCategory.WrittenMaterial,
        [WeenieType.Key] = CloudInventoryCategory.Keys,
        [WeenieType.Lockpick] = CloudInventoryCategory.Keys,
        [WeenieType.Portal] = CloudInventoryCategory.Portals,
        [WeenieType.ManaStone] = CloudInventoryCategory.ManaStones,
        [WeenieType.LifeStone] = CloudInventoryCategory.LifeStones,
        [WeenieType.CraftTool] = CloudInventoryCategory.CraftingMaterials,
    };

    public static CloudInventoryCategory Classify(ItemType itemType, WeenieType weenieType)
    {
        foreach (var (flag, category) in ItemTypePriority)
        {
            if ((itemType & flag) == flag)
            {
                return category;
            }
        }

        return WeenieTypeFallback.TryGetValue(weenieType, out var fallbackCategory)
            ? fallbackCategory
            : CloudInventoryCategory.Miscellaneous;
    }
}
