namespace ACE.Cloud.Domain;

/// <summary>
/// Turns <see cref="CloudIconCompositionInputs"/> into an ordered <see cref="CloudIconLayerPlan"/>
/// without resolving or decoding any bytes (UI-005, UI-006). Reproduces
/// <c>WorldObject_Networking.CalculateObjDesc()</c>'s clothing/palette selection rule: if
/// <see cref="CloudIconCompositionInputs.ClothingBaseDid"/> resolves an effect for
/// <see cref="CloudIconCompositionInputs.SetupTableId"/> and that effect carries a non-zero icon
/// override that <see cref="CloudIconCompositionInputs.IgnoreCloIcons"/> does not suppress, the
/// override replaces <see cref="CloudIconCompositionInputs.BaseIconDid"/>; the clothing effect's
/// palette overrides apply to whichever base icon is ultimately used. Layer order matches issue #24's
/// validated contract with static UiEffects appended last (CONTEXT.md).
/// </summary>
public static class CloudIconLayerPlanner
{
    public static async Task<CloudIconLayerPlan> PlanAsync(
        CloudIconCompositionInputs inputs,
        ICloudIconClothingEffectResolver clothingEffectResolver,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(clothingEffectResolver);

        var effectiveBaseIconDid = inputs.BaseIconDid ?? 0;
        var paletteOverrides = (IReadOnlyList<CloudIconPaletteRangeOverride>)Array.Empty<CloudIconPaletteRangeOverride>();

        if (inputs.ClothingBaseDid is { } clothingBaseDid && clothingBaseDid != 0)
        {
            var resolution = await clothingEffectResolver.ResolveAsync(
                clothingBaseDid, inputs.SetupTableId, inputs.PaletteTemplate, inputs.Shade, cancellationToken);

            if (resolution is not null)
            {
                paletteOverrides = resolution.PaletteOverrides;

                if (resolution.IconOverrideDid is { } overrideDid && !inputs.IgnoreCloIcons)
                {
                    effectiveBaseIconDid = overrideDid;
                }
            }
        }

        var baseIcon = new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, effectiveBaseIconDid);

        var layers = new List<CloudIconLayerReference>();

        if (inputs.ItemTypeBackgroundDid is { } backgroundDid && backgroundDid != 0)
        {
            layers.Add(new CloudIconLayerReference(CloudIconLayerKind.Background, backgroundDid));
        }

        if (inputs.UnderlayDid is { } underlayDid && underlayDid != 0)
        {
            layers.Add(new CloudIconLayerReference(CloudIconLayerKind.Underlay, underlayDid));
        }

        layers.Add(baseIcon);

        if (inputs.OverlayDid is { } overlayDid && overlayDid != 0)
        {
            layers.Add(new CloudIconLayerReference(CloudIconLayerKind.Overlay, overlayDid));
        }

        if (inputs.OverlaySecondaryDid is { } overlaySecondaryDid && overlaySecondaryDid != 0)
        {
            layers.Add(new CloudIconLayerReference(CloudIconLayerKind.OverlaySecondary, overlaySecondaryDid));
        }

        foreach (var uiEffectDid in inputs.UiEffectDids)
        {
            if (uiEffectDid != 0)
            {
                layers.Add(new CloudIconLayerReference(CloudIconLayerKind.UiEffect, uiEffectDid));
            }
        }

        return new CloudIconLayerPlan(baseIcon, paletteOverrides, layers);
    }
}
