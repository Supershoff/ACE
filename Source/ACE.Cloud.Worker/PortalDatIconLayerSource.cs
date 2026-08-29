using ACE.Cloud.Domain;

namespace ACE.Cloud.Worker;

/// <summary>
/// The production <see cref="ICloudIconLayerSource"/>: resolves every icon layer -- background,
/// underlay, base icon, overlay, secondary overlay, and UiEffects -- from the active manifest's
/// extracted Texture entries and decodes them with <see cref="CloudIconTexturePixelDecoder"/>
/// (UI-005, ASSET-004).
/// </summary>
public sealed class PortalDatIconLayerSource : ICloudIconLayerSource
{
    private readonly CloudAssetManifestBlobReader _blobReader;

    public PortalDatIconLayerSource(CloudAssetManifestBlobReader blobReader)
    {
        ArgumentNullException.ThrowIfNull(blobReader);
        _blobReader = blobReader;
    }

    public async Task<CloudIconLayerResolution> ResolveAsync(
        CloudIconLayerReference reference,
        IReadOnlyList<CloudIconPaletteRangeOverride> paletteOverrides,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paletteOverrides);

        var rawTextureBytes = await _blobReader.TryReadAsync(reference.Did, CloudAssetFileKind.Texture, cancellationToken);
        if (rawTextureBytes is null)
        {
            return CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Missing);
        }

        return await CloudIconTexturePixelDecoder.DecodeAsync(
            rawTextureBytes,
            paletteOverrides,
            (paletteDid, ct) => _blobReader.TryReadAsync(paletteDid, CloudAssetFileKind.Palette, ct),
            cancellationToken);
    }
}
