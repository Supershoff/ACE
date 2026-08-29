namespace ACE.Cloud.Domain;

/// <summary>
/// The result of one <see cref="CloudIconCompositor"/> run: either the composed raster, or the
/// neutral <see cref="CloudIconCompositionOutcomeKind.Fallback"/> plus every diagnostic that caused
/// it (UI-006). <see cref="CacheKey"/> is always populated -- including for a fallback -- so a fallback
/// result is just as cacheable by manifest version as a successful one.
/// </summary>
public sealed record CloudIconCompositionResult
{
    public CloudIconCompositionOutcomeKind Outcome { get; }

    public CloudIconCompositionCacheKey CacheKey { get; }

    public CloudIconRasterLayer? ComposedRaster { get; }

    public IReadOnlyList<CloudIconCompositionDiagnostic> Diagnostics { get; }

    private CloudIconCompositionResult(
        CloudIconCompositionOutcomeKind outcome,
        CloudIconCompositionCacheKey cacheKey,
        CloudIconRasterLayer? composedRaster,
        IReadOnlyList<CloudIconCompositionDiagnostic> diagnostics)
    {
        Outcome = outcome;
        CacheKey = cacheKey;
        ComposedRaster = composedRaster;
        Diagnostics = diagnostics;
    }

    public static CloudIconCompositionResult Composed(CloudIconCompositionCacheKey cacheKey, CloudIconRasterLayer raster)
    {
        ArgumentNullException.ThrowIfNull(raster);
        return new CloudIconCompositionResult(
            CloudIconCompositionOutcomeKind.Composed, cacheKey, raster, Array.Empty<CloudIconCompositionDiagnostic>());
    }

    public static CloudIconCompositionResult Fallback(
        CloudIconCompositionCacheKey cacheKey, IReadOnlyList<CloudIconCompositionDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (diagnostics.Count == 0)
        {
            throw new ArgumentException("A fallback result requires at least one diagnostic.", nameof(diagnostics));
        }

        return new CloudIconCompositionResult(CloudIconCompositionOutcomeKind.Fallback, cacheKey, null, diagnostics);
    }
}
