using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Cloud.Worker;

namespace ACE.Cloud.Worker.Tests;

/// <summary>
/// The protected golden harness for Icon Reconstruction (UI-005, UI-006, ASSET-005). Following the
/// exact pattern <c>PortalDatAssetExtractorTests</c> established for issue #24/#25: this requires an
/// operator-owned <c>client_portal.dat</c> that is never committed to the repository, so it reports
/// Inconclusive rather than failing when no corpus is configured. Executing this harness for real
/// against a curated fidelity corpus (clothing palette/shade variants, underlays, overlays, tailoring,
/// imbues, magical UiEffects, missing/corrupt references) is explicitly deferred to the #28 human
/// gate; this issue's job is only to prove the harness itself runs end to end against one already-
/// validated reference item and to document the fixture contract for #28 to extend.
///
/// To run this for real, set <c>ACE_CLOUD_MULE_DAT_DIRECTORY</c> to a local directory containing a
/// standard <c>client_portal.dat</c> before running the test.
/// </summary>
[TestClass]
public sealed class CloudIconCompositionGoldenTests
{
    // Issue #24's validated TreeStats WCID 42635 reference item: no ClothingBase/palette variation,
    // just background -> base icon -> overlay (no underlay/secondary/UiEffects for this item).
    private const uint ItemTypeBackgroundDid = 0x060011D3;
    private const uint BaseIconDid = 0x06006C0A;
    private const uint OverlayDid = 0x06006C34;

    [TestMethod]
    public async Task ComposeAsync_TreeStatsReferenceItem_ProducesADeterministicComposedIcon()
    {
        var datDirectory = Environment.GetEnvironmentVariable("ACE_CLOUD_MULE_DAT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(datDirectory))
        {
            Assert.Inconclusive(
                "No local client_portal.dat is configured. Set ACE_CLOUD_MULE_DAT_DIRECTORY to a " +
                "directory containing an operator-owned client_portal.dat to run this golden test. " +
                "Executing the full curated fidelity corpus is owned by issue #28.");
            return;
        }

        var sourcePath = Path.Combine(datDirectory, "client_portal.dat");
        if (!File.Exists(sourcePath))
        {
            Assert.Inconclusive($"ACE_CLOUD_MULE_DAT_DIRECTORY is set, but {sourcePath} does not exist.");
            return;
        }

        var storageRoot = Path.Combine(Path.GetTempPath(), "cloud-icon-golden-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);

        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var manifestId = Guid.NewGuid();
            var entries = await new PortalDatAssetExtractor().ExtractAsync(sourcePath, manifestId, blobStore);

            var relativePathsByKey = entries.ToDictionary(
                e => e.Key, e => CloudAssetStagingPathPolicy.BuildManifestEntryRelativePath(manifestId, e.Key));
            var blobReader = new CloudAssetManifestBlobReader(relativePathsByKey, blobStore);

            var layerSource = new PortalDatIconLayerSource(blobReader);
            var clothingResolver = new PortalDatIconClothingEffectResolver(blobReader);

            var inputs = new CloudIconCompositionInputs
            {
                BaseIconDid = BaseIconDid,
                OverlayDid = OverlayDid,
                ItemTypeBackgroundDid = ItemTypeBackgroundDid,
            };

            var result = await CloudIconCompositor.ComposeAsync(inputs, manifestVersion: 1, clothingResolver, layerSource);

            Assert.AreEqual(
                CloudIconCompositionOutcomeKind.Composed, result.Outcome,
                result.Diagnostics.Count > 0
                    ? $"Composition fell back: {string.Join(", ", result.Diagnostics.Select(d => $"{d.Layer.Kind}:{d.Layer.Did:x8}={d.Reason}"))}"
                    : "Composition unexpectedly fell back with no diagnostics.");

            Assert.IsNotNull(result.ComposedRaster);
            Assert.IsGreaterThan(0, result.ComposedRaster!.Width);
            Assert.IsGreaterThan(0, result.ComposedRaster.Height);

            var pngBytes = CloudIconPngEncoder.Encode(result.ComposedRaster);
            Assert.IsGreaterThan(0, pngBytes.Length);

            var secondPassBytes = CloudIconPngEncoder.Encode(result.ComposedRaster);
            CollectionAssert.AreEqual(pngBytes, secondPassBytes, "Encoding the same raster twice must be bitwise stable.");
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }
}
