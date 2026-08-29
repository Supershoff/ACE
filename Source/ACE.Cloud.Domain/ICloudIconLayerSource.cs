namespace ACE.Cloud.Domain;

/// <summary>
/// Resolves and decodes one icon layer from the active Asset Manifest (UI-005, ASSET-004). The real
/// implementation reads the manifest's extracted Texture entries through protected blob storage and
/// decodes their pixels without touching ACE's process-wide <c>DatManager</c> singleton; tests
/// substitute a fake so <see cref="CloudIconCompositor"/> stays testable without any DAT bytes at all.
/// </summary>
public interface ICloudIconLayerSource
{
    /// <summary>
    /// <paramref name="paletteOverrides"/> is non-empty only when <paramref name="reference"/> is the
    /// plan's base icon layer and its resolved clothing effect carries palette substitution;
    /// implementations apply it to the decoded base icon's own palette before mapping indices to
    /// colors (UI-005). Every other layer always receives an empty list.
    /// </summary>
    Task<CloudIconLayerResolution> ResolveAsync(
        CloudIconLayerReference reference,
        IReadOnlyList<CloudIconPaletteRangeOverride> paletteOverrides,
        CancellationToken cancellationToken = default);
}
