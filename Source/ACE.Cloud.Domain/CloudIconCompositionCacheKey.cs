using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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

    private static readonly Regex WellFormedHexPattern = new("^[0-9a-f]{64}$", RegexOptions.Compiled);

    /// <summary>
    /// Reconstructs a key from its own previously produced <see cref="Hex"/> (issue #31: a web client
    /// only ever holds this persisted string, never a resolved <see cref="CloudIconLayerPlan"/>).
    /// Strictly validates the exact shape <see cref="Create"/> always produces -- 64 lowercase hex
    /// characters, the lowercase form of a SHA-256 digest -- and rejects everything else, so this can
    /// never become a way to turn an arbitrary caller-supplied string into a blob-store path.
    /// </summary>
    public static CloudIconCompositionCacheKey FromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex) || !WellFormedHexPattern.IsMatch(hex))
        {
            throw new ArgumentException("An icon composition cache key must be exactly 64 lowercase hex characters.", nameof(hex));
        }

        return new CloudIconCompositionCacheKey(hex);
    }

    public override string ToString() => Hex;
}
