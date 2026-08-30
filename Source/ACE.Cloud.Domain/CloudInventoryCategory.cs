namespace ACE.Cloud.Domain;

/// <summary>
/// A single normalized display grouping for one Cloud Item (UI-001, CONTEXT.md's "Inventory
/// Category": "derived primarily from ACE ItemType flags with deterministic priority and a
/// WeenieType fallback"). Every value here is a Mule Page grouping key
/// (<see cref="CloudMulePagePolicy"/>); <see cref="CloudInventoryCategoryClassifier"/> is the single
/// place that decides which one a given item belongs to, and it always returns exactly one value --
/// never none, never more than one -- which is what "every visible item belongs to exactly one
/// documented Inventory Category" (issue #30 acceptance criterion) requires.
/// </summary>
public enum CloudInventoryCategory
{
    MeleeWeapons,
    MissileWeapons,
    Casters,
    Armor,
    Clothing,
    Jewelry,
    Foodstuffs,
    Currency,
    Gems,
    SpellComponents,
    WrittenMaterial,
    Keys,
    Portals,
    ManaStones,
    PromissoryNotes,
    LifeStones,
    CraftingMaterials,

    /// <summary>
    /// The documented catch-all for every item whose ItemType flags and WeenieType fallback both
    /// fail to match a more specific category above (issue #30 Red: "unknown types"): for example
    /// ItemType.Misc/Useless/Container/Lockable/Service/MagicWieldable/Gameboard/Creature/None, or
    /// a WeenieType this classifier does not otherwise recognize. Still exactly one category, never
    /// an error or a missing grid placement.
    /// </summary>
    Miscellaneous,
}
