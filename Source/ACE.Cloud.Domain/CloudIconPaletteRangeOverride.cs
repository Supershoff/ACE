namespace ACE.Cloud.Domain;

/// <summary>
/// One contiguous run of palette color indices a resolved <c>ClothingSubPalEffect</c> substitutes
/// into the base icon's own palette, mirroring <c>CloSubPaletteRange</c>'s <c>Offset</c>/
/// <c>NumColors</c> shape (UI-005: "PaletteTemplate, Shade values"). <see cref="PaletteDid"/> is
/// already the shade-resolved <c>Palette</c> DID (ACE: <c>PaletteSet.GetPaletteID(shade)</c>'s
/// result) -- resolving *which* palette a <c>PaletteSet</c> + shade selects is
/// <see cref="ICloudIconClothingEffectResolver"/>'s job, not the compositor's, so this record only
/// ever carries a single already-resolved reference plus the index range to substitute.
/// </summary>
public readonly record struct CloudIconPaletteRangeOverride
{
    public uint PaletteDid { get; }

    public ushort Offset { get; }

    public ushort NumColors { get; }

    public CloudIconPaletteRangeOverride(uint paletteDid, ushort offset, ushort numColors)
    {
        if (paletteDid == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(paletteDid), "A palette range override requires a real resolved Palette DID.");
        }

        if (numColors == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(numColors), "A palette range override requires at least one color.");
        }

        PaletteDid = paletteDid;
        Offset = offset;
        NumColors = numColors;
    }
}
