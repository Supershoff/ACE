using System.Collections.Concurrent;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Worker;

/// <summary>
/// One resolved icon composition cache lookup (UI-006). <see cref="PngBytes"/> is populated only for
/// <see cref="CloudIconCompositionOutcomeKind.Composed"/>: a fallback result has nothing to persist,
/// since a fixed neutral-fallback image is a static asset the caller already owns, not a per-item
/// derivative.
/// </summary>
public sealed record CloudIconCompositionCacheEntry(
    CloudIconCompositionCacheKey CacheKey,
    CloudIconCompositionOutcomeKind Outcome,
    byte[]? PngBytes,
    IReadOnlyList<CloudIconCompositionDiagnostic> Diagnostics);

/// <summary>
/// Content-addressed caching for Icon Reconstruction's web-ready PNG derivatives (UI-006). Two
/// concerns are layered here: (1) in-process coalescing, so N concurrent requests for the same
/// composition run the (clothing resolution + layer decode + compositing) pipeline exactly once
/// rather than once per caller; and (2) durable persistence through the same
/// <see cref="IProtectedAssetBlobStore"/> every other protected asset uses, keyed only by
/// <see cref="CloudIconCompositionCacheKey"/> (<see cref="CloudAssetStagingPathPolicy.BuildIconCompositionCacheRelativePath"/>),
/// so a cache hit survives process restarts and a manifest-version bump automatically invalidates
/// stale entries by changing the key itself (ASSET-004: "Namespace or invalidate generated assets
/// when the input DAT changes") rather than requiring an explicit purge. There is no separate
/// database row for the cache: identical composition inputs always compose to bitwise-identical PNG
/// bytes, so a lost or duplicate concurrent write to the same blob path can never disagree with
/// itself (UI-006: "Cache hits are bitwise stable for identical complete composition keys").
/// </summary>
public sealed class CloudIconCompositionCache
{
    private readonly IProtectedAssetBlobStore _blobStore;
    private readonly ConcurrentDictionary<string, Task<CloudIconCompositionCacheEntry>> _inFlight = new();

    public CloudIconCompositionCache(IProtectedAssetBlobStore blobStore)
    {
        ArgumentNullException.ThrowIfNull(blobStore);
        _blobStore = blobStore;
    }

    public async Task<CloudIconCompositionCacheEntry> GetOrComposeAsync(
        CloudIconCompositionInputs inputs,
        int manifestVersion,
        ICloudIconClothingEffectResolver clothingEffectResolver,
        ICloudIconLayerSource layerSource,
        CancellationToken cancellationToken = default)
    {
        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, clothingEffectResolver, cancellationToken);
        var cacheKey = CloudIconCompositionCacheKey.Create(plan, manifestVersion);

        var relativePath = CloudAssetStagingPathPolicy.BuildIconCompositionCacheRelativePath(cacheKey);
        if (await _blobStore.ExistsAsync(relativePath, cancellationToken))
        {
            var cachedBytes = await ReadAllBytesAsync(relativePath, cancellationToken);
            return new CloudIconCompositionCacheEntry(
                cacheKey, CloudIconCompositionOutcomeKind.Composed, cachedBytes, Array.Empty<CloudIconCompositionDiagnostic>());
        }

        var composeTask = _inFlight.GetOrAdd(
            cacheKey.Hex,
            _ => ComposeAndPersistAsync(inputs, manifestVersion, cacheKey, relativePath, clothingEffectResolver, layerSource, cancellationToken));

        try
        {
            return await composeTask;
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<string, Task<CloudIconCompositionCacheEntry>>(cacheKey.Hex, composeTask));
        }
    }

    private async Task<CloudIconCompositionCacheEntry> ComposeAndPersistAsync(
        CloudIconCompositionInputs inputs,
        int manifestVersion,
        CloudIconCompositionCacheKey cacheKey,
        string relativePath,
        ICloudIconClothingEffectResolver clothingEffectResolver,
        ICloudIconLayerSource layerSource,
        CancellationToken cancellationToken)
    {
        var result = await CloudIconCompositor.ComposeAsync(inputs, manifestVersion, clothingEffectResolver, layerSource, cancellationToken);

        if (result.Outcome == CloudIconCompositionOutcomeKind.Fallback)
        {
            return new CloudIconCompositionCacheEntry(cacheKey, CloudIconCompositionOutcomeKind.Fallback, null, result.Diagnostics);
        }

        var pngBytes = CloudIconPngEncoder.Encode(result.ComposedRaster!);

        if (!await _blobStore.ExistsAsync(relativePath, cancellationToken))
        {
            await _blobStore.WriteAsync(relativePath, pngBytes, cancellationToken);
        }

        return new CloudIconCompositionCacheEntry(
            cacheKey, CloudIconCompositionOutcomeKind.Composed, pngBytes, Array.Empty<CloudIconCompositionDiagnostic>());
    }

    private async Task<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        await using var stream = await _blobStore.OpenReadAsync(relativePath, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
