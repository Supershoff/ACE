namespace ACE.Cloud.Domain;

/// <summary>
/// Resolves an item's <c>ClothingBase</c> effect against the active Asset Manifest (UI-005). The real
/// implementation reads the manifest's extracted Clothing (0x10) and PaletteSet (0x0F) entries; tests
/// substitute a fake so <see cref="CloudIconLayerPlanner"/> stays a pure, DI-free function of its
/// inputs plus this one seam.
/// </summary>
public interface ICloudIconClothingEffectResolver
{
    /// <summary>
    /// Returns null when <paramref name="clothingBaseDid"/> has no effect registered for
    /// <paramref name="setupTableId"/> at all (ACE: <c>item.ClothingBaseEffects.ContainsKey(SetupTableId)</c>
    /// was false), which is a legitimate "clothing base does not apply to this model" outcome, not a
    /// missing-reference diagnostic. A real implementation also returns null -- rather than throwing
    /// or surfacing a diagnostic -- when the Clothing/PaletteSet/Palette chain itself cannot be read:
    /// the resulting safe degrade is the item's own plain base icon, which is still that item's real,
    /// correct icon (just without a clothing skin variant), not a "plausible but wrong" one.
    /// </summary>
    Task<CloudIconClothingResolution?> ResolveAsync(
        uint clothingBaseDid,
        uint setupTableId,
        int? paletteTemplate,
        float? shade,
        CancellationToken cancellationToken = default);
}
