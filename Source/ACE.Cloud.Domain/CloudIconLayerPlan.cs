namespace ACE.Cloud.Domain;

/// <summary>
/// The deterministic, fully ordered set of layers one <see cref="CloudIconCompositionInputs"/>
/// requires (UI-005). <see cref="Layers"/> already contains every layer, including
/// <see cref="BaseIcon"/>, in final draw order; <see cref="BaseIcon"/> and
/// <see cref="BaseIconPaletteOverrides"/> are also exposed individually because only the base icon
/// layer receives palette substitution.
/// </summary>
public sealed record CloudIconLayerPlan
{
    public CloudIconLayerReference BaseIcon { get; }

    public IReadOnlyList<CloudIconPaletteRangeOverride> BaseIconPaletteOverrides { get; }

    public IReadOnlyList<CloudIconLayerReference> Layers { get; }

    public CloudIconLayerPlan(
        CloudIconLayerReference baseIcon,
        IReadOnlyList<CloudIconPaletteRangeOverride> baseIconPaletteOverrides,
        IReadOnlyList<CloudIconLayerReference> layers)
    {
        ArgumentNullException.ThrowIfNull(baseIconPaletteOverrides);
        ArgumentNullException.ThrowIfNull(layers);

        if (baseIcon.Kind != CloudIconLayerKind.BaseIcon)
        {
            throw new ArgumentException("The plan's base icon reference must be of kind BaseIcon.", nameof(baseIcon));
        }

        if (!layers.Contains(baseIcon))
        {
            throw new ArgumentException("The plan's layer list must include its own base icon reference.", nameof(layers));
        }

        BaseIcon = baseIcon;
        BaseIconPaletteOverrides = baseIconPaletteOverrides;
        Layers = layers;
    }
}
