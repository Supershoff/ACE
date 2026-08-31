using ACE.Entity.Enum;

namespace ACE.Cloud.Domain;

/// <summary>
/// Resolves the two <see cref="CloudIconCompositionInputs"/> fields that are not item weenie
/// properties -- <see cref="CloudIconCompositionInputs.ItemTypeBackgroundDid"/> and
/// <see cref="CloudIconCompositionInputs.UiEffectDids"/> (issue #24: "Select the shared background
/// DID from the item's ItemType; it is not an item icon property") -- from an operator-supplied
/// mapping rather than fabricating or hard-coding any specific item's DID (issue #34 human-acceptance
/// correction: "without hard-coding WCID 42635 or assuming stock DAT contents"). Pure and I/O-free
/// (ARCH-012): the caller supplies both the item's already-known ItemType/WeenieType/UiEffects and the
/// operator's configured mapping, so this can run identically at deposit time (a live WorldObject) or
/// backfill time (a retained biota), same as <see cref="CloudInventoryCategoryClassifier"/> already
/// does for the Mule Page grid.
/// </summary>
public static class CloudIconSharedOverlayResolver
{
    /// <summary>
    /// Reuses <see cref="CloudInventoryCategoryClassifier"/> -- the same documented, deterministic
    /// ItemType priority the Mule Page grid already uses -- as the key into the operator's background
    /// mapping, rather than inventing a second, competing priority order for icon backgrounds.
    /// </summary>
    public static uint? ResolveItemTypeBackgroundDid(
        ItemType itemType,
        WeenieType weenieType,
        IReadOnlyDictionary<string, uint> backgroundDidsByCategory)
    {
        ArgumentNullException.ThrowIfNull(backgroundDidsByCategory);

        var category = CloudInventoryCategoryClassifier.Classify(itemType, weenieType);

        return backgroundDidsByCategory.TryGetValue(category.ToString(), out var did) && did != 0
            ? did
            : null;
    }

    /// <summary>
    /// The fixed, documented order still/imbue-glow overlays are drawn in when an item carries more
    /// than one active <see cref="UiEffects"/> flag at once -- ascending bit value, matching
    /// <see cref="UiEffects"/>'s own declared order. <see cref="CloudIconLayerPlanner"/> already draws
    /// every returned DID last, in this list's order (UI-006: "magical glow is a still blue layer").
    /// </summary>
    private static readonly UiEffects[] OrderedUiEffectFlags =
    [
        UiEffects.Magical,
        UiEffects.Poisoned,
        UiEffects.BoostHealth,
        UiEffects.BoostMana,
        UiEffects.BoostStamina,
        UiEffects.Fire,
        UiEffects.Lightning,
        UiEffects.Frost,
        UiEffects.Acid,
        UiEffects.Bludgeoning,
        UiEffects.Slashing,
        UiEffects.Piercing,
        UiEffects.Nether,
    ];

    public static IReadOnlyList<uint> ResolveUiEffectDids(
        UiEffects? uiEffects,
        IReadOnlyDictionary<string, uint> overlayDidsByEffect)
    {
        ArgumentNullException.ThrowIfNull(overlayDidsByEffect);

        if (uiEffects is null || uiEffects.Value == UiEffects.Undef)
        {
            return Array.Empty<uint>();
        }

        List<uint>? dids = null;

        foreach (var flag in OrderedUiEffectFlags)
        {
            if ((uiEffects.Value & flag) != flag)
            {
                continue;
            }

            if (!overlayDidsByEffect.TryGetValue(flag.ToString(), out var did) || did == 0)
            {
                continue;
            }

            dids ??= [];
            dids.Add(did);
        }

        return (IReadOnlyList<uint>)dids ?? Array.Empty<uint>();
    }
}
