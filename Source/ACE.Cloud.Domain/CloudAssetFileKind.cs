namespace ACE.Cloud.Domain;

/// <summary>
/// The DAT file-type category one extracted manifest entry belongs to (ASSET-004). <see cref="Palette"/>,
/// <see cref="PaletteSet"/>, and <see cref="Clothing"/> were added by issue #26 alongside
/// <see cref="Texture"/>: Icon Reconstruction (UI-005) needs a clothing item's palette/shade
/// resolution chain (Clothing -&gt; PaletteSet -&gt; Palette), and decoding that chain from the
/// versioned manifest -- instead of ACE's process-wide <c>DatManager</c> singleton -- requires the
/// manifest to carry those file kinds too, not just Texture.
/// </summary>
public enum CloudAssetFileKind
{
    Texture,
    Palette,
    PaletteSet,
    Clothing,
}
