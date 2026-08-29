namespace ACE.Cloud.Domain;

/// <summary>
/// Composes a <see cref="CloudIconLayerPlan"/>'s layers into one deterministic static raster, or
/// falls back with diagnostics (UI-005, UI-006). Any single layer failing to resolve -- for whatever
/// reason -- turns the *entire* result into the neutral fallback rather than a partial icon: a
/// missing overlay silently omitted would itself be "a plausible but incorrect icon" (CONTEXT.md), so
/// there is no partial-success path here, unlike the Custodian deposit batch rule elsewhere in this
/// codebase (that asymmetry is intentional, not an oversight). Composition is pure integer "over"
/// alpha blending in <see cref="CloudIconLayerPlan.Layers"/> order, so identical inputs always
/// produce bitwise-identical output (UI-006: "Cache hits are bitwise stable").
/// </summary>
public static class CloudIconCompositor
{
    public static async Task<CloudIconCompositionResult> ComposeAsync(
        CloudIconCompositionInputs inputs,
        int manifestVersion,
        ICloudIconClothingEffectResolver clothingEffectResolver,
        ICloudIconLayerSource layerSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(clothingEffectResolver);
        ArgumentNullException.ThrowIfNull(layerSource);

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, clothingEffectResolver, cancellationToken);
        var cacheKey = CloudIconCompositionCacheKey.Create(plan, manifestVersion);

        var diagnostics = new List<CloudIconCompositionDiagnostic>();
        var resolvedRasters = new List<CloudIconRasterLayer>(plan.Layers.Count);

        foreach (var layer in plan.Layers)
        {
            if (layer.IsUnresolvable)
            {
                diagnostics.Add(new CloudIconCompositionDiagnostic(layer, CloudIconLayerResolutionOutcomeKind.Missing));
                continue;
            }

            var paletteOverrides = layer.Equals(plan.BaseIcon) ? plan.BaseIconPaletteOverrides : Array.Empty<CloudIconPaletteRangeOverride>();
            var resolution = await layerSource.ResolveAsync(layer, paletteOverrides, cancellationToken);

            if (resolution.Outcome != CloudIconLayerResolutionOutcomeKind.Resolved)
            {
                diagnostics.Add(new CloudIconCompositionDiagnostic(layer, resolution.Outcome));
                continue;
            }

            if (resolvedRasters.Count > 0
                && (resolution.Raster!.Width != resolvedRasters[0].Width || resolution.Raster.Height != resolvedRasters[0].Height))
            {
                diagnostics.Add(new CloudIconCompositionDiagnostic(layer, CloudIconLayerResolutionOutcomeKind.Corrupt));
                continue;
            }

            resolvedRasters.Add(resolution.Raster!);
        }

        if (diagnostics.Count > 0)
        {
            return CloudIconCompositionResult.Fallback(cacheKey, diagnostics);
        }

        var composed = ComposeOver(resolvedRasters);
        return CloudIconCompositionResult.Composed(cacheKey, composed);
    }

    private static CloudIconRasterLayer ComposeOver(IReadOnlyList<CloudIconRasterLayer> layers)
    {
        var width = layers[0].Width;
        var height = layers[0].Height;
        var canvas = new byte[width * height * 4];

        foreach (var layer in layers)
        {
            var src = layer.Rgba;
            for (var pixel = 0; pixel < width * height; pixel++)
            {
                var offset = pixel * 4;
                var srcR = src[offset];
                var srcG = src[offset + 1];
                var srcB = src[offset + 2];
                var srcA = src[offset + 3];

                if (srcA == 0)
                {
                    continue;
                }

                if (srcA == 255)
                {
                    canvas[offset] = srcR;
                    canvas[offset + 1] = srcG;
                    canvas[offset + 2] = srcB;
                    canvas[offset + 3] = 255;
                    continue;
                }

                var dstR = canvas[offset];
                var dstG = canvas[offset + 1];
                var dstB = canvas[offset + 2];
                var dstA = canvas[offset + 3];

                var outA = srcA + (dstA * (255 - srcA) / 255);
                if (outA == 0)
                {
                    continue;
                }

                canvas[offset] = (byte)(((srcR * srcA) + (dstR * dstA * (255 - srcA) / 255)) / outA);
                canvas[offset + 1] = (byte)(((srcG * srcA) + (dstG * dstA * (255 - srcA) / 255)) / outA);
                canvas[offset + 2] = (byte)(((srcB * srcA) + (dstB * dstA * (255 - srcA) / 255)) / outA);
                canvas[offset + 3] = (byte)outA;
            }
        }

        return new CloudIconRasterLayer(width, height, canvas);
    }
}
