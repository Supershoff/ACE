namespace ACE.Cloud.Domain;

/// <summary>
/// Why one requested <see cref="CloudIconLayerReference"/> did or did not produce usable pixels
/// (UI-006, ASSET-004: "Test missing, corrupt, unsupported, oversized, and malicious references").
/// Every value except <see cref="Resolved"/> both records an explicit admin diagnostic and forces the
/// whole composition to its neutral fallback (CONTEXT.md: "rather than silently displaying a
/// plausible but incorrect icon") rather than rendering a partial icon that is missing a layer.
/// </summary>
public enum CloudIconLayerResolutionOutcomeKind
{
    /// <summary>The reference resolved to a usable, decoded raster layer.</summary>
    Resolved,

    /// <summary>No manifest entry exists for the requested DID/kind.</summary>
    Missing,

    /// <summary>A manifest entry exists, but its bytes do not parse as the declared file format.</summary>
    Corrupt,

    /// <summary>The bytes parsed, but their pixel format is not one the compositor decodes.</summary>
    Unsupported,

    /// <summary>The bytes parsed to dimensions larger than an icon layer can plausibly be.</summary>
    Oversized,

    /// <summary>
    /// The bytes parsed, but contain a value that, if trusted, would read outside the data a
    /// well-formed file of this kind could ever contain (for example a palette index beyond the
    /// resolved palette's color count, or a declared size whose buffer computation overflows).
    /// </summary>
    Malicious,
}
