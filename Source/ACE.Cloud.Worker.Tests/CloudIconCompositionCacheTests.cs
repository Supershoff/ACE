using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Cloud.Worker;

namespace ACE.Cloud.Worker.Tests;

/// <summary>
/// UI-006 Red/Green coverage: "Return web-ready derivatives through content-addressed caching" and
/// "Test... concurrent render/cache requests" / "Cache hits are bitwise stable for identical complete
/// composition keys" -- entirely against an in-memory fake layer source and a temp-directory blob
/// store, no DAT bytes required.
/// </summary>
[TestClass]
public sealed class CloudIconCompositionCacheTests
{
    private const uint BaseIconDid = 0x06000010;

    [TestMethod]
    public async Task GetOrComposeAsync_ComposesAndPersistsAPngDerivative()
    {
        var storageRoot = CreateTempStorageRoot();
        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var cache = new CloudIconCompositionCache(blobStore);
            var inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid };
            var layerSource = new CountingLayerSource(delay: null);
            layerSource.SetResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, 2, 2, 10, 20, 30, 255);

            var entry = await cache.GetOrComposeAsync(inputs, manifestVersion: 1, new FakeClothingResolver(), layerSource);

            Assert.AreEqual(CloudIconCompositionOutcomeKind.Composed, entry.Outcome);
            Assert.IsNotNull(entry.PngBytes);
            CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, entry.PngBytes![..4]);

            var relativePath = CloudAssetStagingPathPolicy.BuildIconCompositionCacheRelativePath(entry.CacheKey);
            Assert.IsTrue(await blobStore.ExistsAsync(relativePath));
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetOrComposeAsync_SecondCallForTheSameCompositionServesTheBlobCacheWithoutRecomposing()
    {
        var storageRoot = CreateTempStorageRoot();
        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var cache = new CloudIconCompositionCache(blobStore);
            var inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid };
            var layerSource = new CountingLayerSource(delay: null);
            layerSource.SetResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, 2, 2, 10, 20, 30, 255);

            var first = await cache.GetOrComposeAsync(inputs, 1, new FakeClothingResolver(), layerSource);
            var callsAfterFirst = layerSource.ResolveCallCount;

            var second = await cache.GetOrComposeAsync(inputs, 1, new FakeClothingResolver(), layerSource);

            Assert.AreEqual(callsAfterFirst, layerSource.ResolveCallCount, "Expected the second call to be served from the blob cache.");
            CollectionAssert.AreEqual(first.PngBytes, second.PngBytes);
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetOrComposeAsync_ManyConcurrentIdenticalRequests_ComposeOnceAndReturnBitwiseIdenticalBytes()
    {
        var storageRoot = CreateTempStorageRoot();
        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var cache = new CloudIconCompositionCache(blobStore);
            var inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid };
            var layerSource = new CountingLayerSource(delay: TimeSpan.FromMilliseconds(50));
            layerSource.SetResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, 2, 2, 1, 2, 3, 255);

            var tasks = Enumerable.Range(0, 16)
                .Select(_ => cache.GetOrComposeAsync(inputs, 1, new FakeClothingResolver(), layerSource));
            var entries = await Task.WhenAll(tasks);

            Assert.IsTrue(layerSource.ResolveCallCount <= 2, $"Expected at most one in-flight compose to actually resolve layers, saw {layerSource.ResolveCallCount} resolutions.");
            foreach (var entry in entries)
            {
                Assert.AreEqual(entries[0].CacheKey, entry.CacheKey);
                CollectionAssert.AreEqual(entries[0].PngBytes, entry.PngBytes);
            }
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetOrComposeAsync_UnresolvableComposition_ReturnsFallbackAndWritesNoBlob()
    {
        var storageRoot = CreateTempStorageRoot();
        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var cache = new CloudIconCompositionCache(blobStore);
            var inputs = new CloudIconCompositionInputs(); // no base icon, no clothing base: unresolvable

            var entry = await cache.GetOrComposeAsync(inputs, 1, new FakeClothingResolver(), new CountingLayerSource(delay: null));

            Assert.AreEqual(CloudIconCompositionOutcomeKind.Fallback, entry.Outcome);
            Assert.IsNull(entry.PngBytes);
            Assert.AreEqual(1, entry.Diagnostics.Count);

            var relativePath = CloudAssetStagingPathPolicy.BuildIconCompositionCacheRelativePath(entry.CacheKey);
            Assert.IsFalse(await blobStore.ExistsAsync(relativePath));
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetOrComposeAsync_BackgroundBaseOverlayAndMagicalUiEffect_AlterTheCacheKeyAndComposedBytes()
    {
        // Issue #34's follow-up human-acceptance correction: ItemTypeBackgroundDid and UiEffectDids
        // used to be dropped before ever reaching composition; this proves that once resolved and
        // supplied, all four layers (background, base, overlay, and a final magical UiEffect) actually
        // participate in the composed cache key/bytes, not just the base icon.
        var storageRoot = CreateTempStorageRoot();
        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var cache = new CloudIconCompositionCache(blobStore);

            const uint overlayDid = 0x06006C34;
            const uint backgroundDid = 0x060011D3;
            const uint uiEffectDid = 0x06000777;

            var baseOnly = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid };
            var everyLayer = new CloudIconCompositionInputs
            {
                BaseIconDid = BaseIconDid,
                OverlayDid = overlayDid,
                ItemTypeBackgroundDid = backgroundDid,
                UiEffectDids = [uiEffectDid],
            };

            var layerSource = new CountingLayerSource(delay: null);
            layerSource.SetResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, 2, 2, 10, 20, 30, 255);
            layerSource.SetResolved(CloudIconLayerKind.Overlay, overlayDid, 2, 2, 40, 50, 60, 255);
            layerSource.SetResolved(CloudIconLayerKind.Background, backgroundDid, 2, 2, 70, 80, 90, 255);
            layerSource.SetResolved(CloudIconLayerKind.UiEffect, uiEffectDid, 2, 2, 0, 0, 255, 128);

            var baseOnlyEntry = await cache.GetOrComposeAsync(baseOnly, 1, new FakeClothingResolver(), layerSource);
            var everyLayerEntry = await cache.GetOrComposeAsync(everyLayer, 1, new FakeClothingResolver(), layerSource);

            Assert.AreEqual(CloudIconCompositionOutcomeKind.Composed, baseOnlyEntry.Outcome);
            Assert.AreEqual(CloudIconCompositionOutcomeKind.Composed, everyLayerEntry.Outcome);
            Assert.AreNotEqual(baseOnlyEntry.CacheKey, everyLayerEntry.CacheKey);
            CollectionAssert.AreNotEqual(baseOnlyEntry.PngBytes, everyLayerEntry.PngBytes);
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetOrComposeAsync_SameDidUnderADifferentManifestVersion_NeverServesTheOldManifestsBytes()
    {
        // Issue #28's Red requirement: "cache poisoning attempt". Simulates a DAT re-import (ASSET-002
        // reprocessing/upgrade) that changes the bytes behind the exact same DID: composing under the
        // new manifest version must never return the previous manifest version's cached PNG, even
        // though every other composition input (the DID itself) is identical.
        var storageRoot = CreateTempStorageRoot();
        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var cache = new CloudIconCompositionCache(blobStore);
            var inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid };

            var oldManifestLayerSource = new CountingLayerSource(delay: null);
            oldManifestLayerSource.SetResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, 2, 2, 10, 20, 30, 255);
            var fromOldManifest = await cache.GetOrComposeAsync(inputs, manifestVersion: 1, new FakeClothingResolver(), oldManifestLayerSource);

            // A reprocessed/upgraded manifest (version 2) resolves the exact same DID to different bytes.
            var newManifestLayerSource = new CountingLayerSource(delay: null);
            newManifestLayerSource.SetResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, 2, 2, 200, 100, 50, 255);
            var fromNewManifest = await cache.GetOrComposeAsync(inputs, manifestVersion: 2, new FakeClothingResolver(), newManifestLayerSource);

            Assert.AreNotEqual(fromOldManifest.CacheKey, fromNewManifest.CacheKey, "Different manifest versions must never collide onto the same cache key.");
            CollectionAssert.AreNotEqual(fromOldManifest.PngBytes, fromNewManifest.PngBytes, "The new manifest version's composition must never be poisoned by the old manifest version's cached bytes.");

            // Re-requesting the old manifest version afterward must still serve its own untouched bytes.
            var oldManifestAgain = await cache.GetOrComposeAsync(inputs, manifestVersion: 1, new FakeClothingResolver(), oldManifestLayerSource);
            CollectionAssert.AreEqual(fromOldManifest.PngBytes, oldManifestAgain.PngBytes);
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    private static string CreateTempStorageRoot()
    {
        var storageRoot = Path.Combine(Path.GetTempPath(), "cloud-icon-cache-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);
        return storageRoot;
    }

    private sealed class FakeClothingResolver : ICloudIconClothingEffectResolver
    {
        public Task<CloudIconClothingResolution?> ResolveAsync(
            uint clothingBaseDid, uint setupTableId, int? paletteTemplate, float? shade, CancellationToken cancellationToken = default)
            => Task.FromResult<CloudIconClothingResolution?>(null);
    }

    private sealed class CountingLayerSource : ICloudIconLayerSource
    {
        private readonly TimeSpan? _delay;
        private readonly Dictionary<CloudIconLayerReference, CloudIconRasterLayer> _rasters = new();

        public CountingLayerSource(TimeSpan? delay)
        {
            _delay = delay;
        }

        private int _resolveCallCount;

        public int ResolveCallCount => _resolveCallCount;

        public void SetResolved(CloudIconLayerKind kind, uint did, int width, int height, byte r, byte g, byte b, byte a)
        {
            var rgba = new byte[width * height * 4];
            for (var pixel = 0; pixel < width * height; pixel++)
            {
                rgba[(pixel * 4) + 0] = r;
                rgba[(pixel * 4) + 1] = g;
                rgba[(pixel * 4) + 2] = b;
                rgba[(pixel * 4) + 3] = a;
            }

            _rasters[new CloudIconLayerReference(kind, did)] = new CloudIconRasterLayer(width, height, rgba);
        }

        public async Task<CloudIconLayerResolution> ResolveAsync(
            CloudIconLayerReference reference, IReadOnlyList<CloudIconPaletteRangeOverride> paletteOverrides, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _resolveCallCount);

            if (_delay is { } delay)
            {
                await Task.Delay(delay, cancellationToken);
            }

            return _rasters.TryGetValue(reference, out var raster)
                ? CloudIconLayerResolution.Resolved(raster)
                : CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Missing);
        }
    }
}
