namespace ACE.Cloud.Domain;

/// <summary>
/// The result of resolving one item's <c>ClothingBase</c>/<c>SetupTableId</c>/<c>PaletteTemplate</c>/
/// <c>Shade</c> against the active manifest's Clothing (0x10) data, mirroring
/// <c>WorldObject_Networking.CalculateObjDesc()</c>'s <c>CloSubPalEffect</c> lookup (UI-005). A null
/// <see cref="IconOverrideDid"/> means the matched effect exists but carries no icon override (ACE:
/// <c>itemSubPal.Icon &gt; 0</c> was false), so the caller falls back to the item's own base
/// <c>Icon</c> property while still applying <see cref="PaletteOverrides"/> to whichever icon is used.
/// </summary>
public sealed record CloudIconClothingResolution
{
    public uint? IconOverrideDid { get; }

    public IReadOnlyList<CloudIconPaletteRangeOverride> PaletteOverrides { get; }

    public CloudIconClothingResolution(uint? iconOverrideDid, IReadOnlyList<CloudIconPaletteRangeOverride> paletteOverrides)
    {
        ArgumentNullException.ThrowIfNull(paletteOverrides);

        if (iconOverrideDid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(iconOverrideDid), "An icon override DID must be either null or non-zero.");
        }

        IconOverrideDid = iconOverrideDid;
        PaletteOverrides = paletteOverrides;
    }
}
