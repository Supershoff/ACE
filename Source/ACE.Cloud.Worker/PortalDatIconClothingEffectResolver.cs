using ACE.Cloud.Domain;
using ACE.DatLoader.FileTypes;

namespace ACE.Cloud.Worker;

/// <summary>
/// The production <see cref="ICloudIconClothingEffectResolver"/>: reproduces
/// <c>WorldObject_Networking.CalculateObjDesc()</c>'s clothing/sub-palette selection against the
/// active manifest's extracted Clothing/PaletteSet/Palette entries (UI-005), never ACE's process-wide
/// <c>DatManager</c> singleton. Any manifest lookup or parse failure along the chain degrades to
/// "no effect" (see the interface doc comment) rather than throwing, since a missing clothing variant
/// still leaves the item's own base icon available.
/// </summary>
public sealed class PortalDatIconClothingEffectResolver : ICloudIconClothingEffectResolver
{
    private readonly CloudAssetManifestBlobReader _blobReader;

    public PortalDatIconClothingEffectResolver(CloudAssetManifestBlobReader blobReader)
    {
        ArgumentNullException.ThrowIfNull(blobReader);
        _blobReader = blobReader;
    }

    public async Task<CloudIconClothingResolution?> ResolveAsync(
        uint clothingBaseDid, uint setupTableId, int? paletteTemplate, float? shade, CancellationToken cancellationToken = default)
    {
        var clothingTable = await TryReadAsync<ClothingTable>(clothingBaseDid, CloudAssetFileKind.Clothing, cancellationToken);
        if (clothingTable is null || !clothingTable.ClothingBaseEffects.ContainsKey(setupTableId))
        {
            return null;
        }

        if (clothingTable.ClothingSubPalEffects.Count == 0 || (paletteTemplate is null && shade is null))
        {
            return null;
        }

        var paletteOptionKey = (uint)(paletteTemplate ?? 0);
        var itemSubPal = clothingTable.ClothingSubPalEffects.TryGetValue(paletteOptionKey, out var matched)
            ? matched
            : clothingTable.ClothingSubPalEffects[clothingTable.ClothingSubPalEffects.Keys.First()];

        var paletteOverrides = new List<CloudIconPaletteRangeOverride>();
        var effectiveShade = shade ?? 0f;

        foreach (var subPalette in itemSubPal.CloSubPalettes)
        {
            var paletteSet = await TryReadAsync<PaletteSet>(subPalette.PaletteSet, CloudAssetFileKind.PaletteSet, cancellationToken);
            if (paletteSet is null || paletteSet.PaletteList.Count == 0)
            {
                continue;
            }

            var resolvedPaletteDid = paletteSet.GetPaletteID(effectiveShade);
            if (resolvedPaletteDid == 0)
            {
                continue;
            }

            foreach (var range in subPalette.Ranges)
            {
                if (range.NumColors == 0 || range.Offset > ushort.MaxValue || range.NumColors > ushort.MaxValue)
                {
                    continue;
                }

                // Deliberately NOT divided by 8 here, unlike CalculateObjDesc's PropertiesPalette:
                // that division converts to the network wire's block-count units for streaming a
                // live 3D model's palette swap to the client. Icon Reconstruction instead applies the
                // range directly as raw color-array indices against the decoded icon texture's own
                // palette (CloudIconTexturePixelDecoder), a different representation with no wire
                // encoding involved. This is a documented assumption for the #28 golden harness to
                // confirm against real client-rendered icons.
                paletteOverrides.Add(new CloudIconPaletteRangeOverride(resolvedPaletteDid, (ushort)range.Offset, (ushort)range.NumColors));
            }
        }

        return new CloudIconClothingResolution(itemSubPal.Icon == 0 ? null : itemSubPal.Icon, paletteOverrides);
    }

    private async Task<T?> TryReadAsync<T>(uint did, CloudAssetFileKind kind, CancellationToken cancellationToken)
        where T : ACE.DatLoader.FileTypes.FileType, new()
    {
        if (did == 0)
        {
            return null;
        }

        var bytes = await _blobReader.TryReadAsync(did, kind, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            var value = new T();
            using var reader = new BinaryReader(new MemoryStream(bytes, writable: false));
            value.Unpack(reader);
            return value;
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or ArgumentOutOfRangeException or OverflowException or ArgumentException)
        {
            return null;
        }
    }
}
