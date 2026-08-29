using System.Security.Cryptography;
using System.Text;

namespace ACE.Cloud.Domain;

/// <summary>
/// The complete content/composition cache key UI-006 requires ("Cache icons by a complete content/
/// composition key"). Built only from a resolved <see cref="CloudIconLayerPlan"/> (every layer DID in
/// final draw order, plus the base icon's resolved palette overrides) and the active manifest
/// version -- never from <see cref="CloudIconCompositionInputs"/> fields that do not affect
/// <see cref="CloudIconLayerPlan"/>'s resolution (there are none: every input either already appears
/// as a plan layer or, like <see cref="CloudIconCompositionInputs.IgnoreCloIcons"/>/
/// <see cref="CloudIconCompositionInputs.SetupTableId"/>, only influenced which layers were chosen and
/// so is already reflected in the plan). Stack count, selection, and reservation state have no field
/// on <see cref="CloudIconCompositionInputs"/> to begin with, so they structurally cannot reach this
/// key (UI-006: "Stack quantity, selection, reservation... are separate UI layers"). Two composition
/// requests that resolve to the same plan under the same manifest version always produce the same
/// key; any differing layer DID, palette override, or manifest version always changes it.
/// </summary>
public readonly record struct CloudIconCompositionCacheKey
{
    public string Hex { get; }

    private CloudIconCompositionCacheKey(string hex)
    {
        Hex = hex;
    }

    public static CloudIconCompositionCacheKey Create(CloudIconLayerPlan plan, int manifestVersion)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (manifestVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(manifestVersion));
        }

        var canonical = new StringBuilder();
        canonical.Append("v1|manifest=").Append(manifestVersion).Append('|');

        foreach (var layer in plan.Layers)
        {
            canonical.Append("layer=").Append(layer.Kind).Append(':').Append(layer.Did.ToString("x8")).Append('|');
        }

        canonical.Append("palette=");
        foreach (var range in plan.BaseIconPaletteOverrides)
        {
            canonical.Append(range.PaletteDid.ToString("x8")).Append(':')
                .Append(range.Offset).Append(':').Append(range.NumColors).Append(';');
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return new CloudIconCompositionCacheKey(Convert.ToHexStringLower(hashBytes));
    }

    public override string ToString() => Hex;
}
