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

    /// <summary>
    /// Issue #28's Green requirement: "Add an end-to-end fidelity harness for protected environments"
    /// -- this generalizes <see cref="ComposeAsync_TreeStatsReferenceItem_ProducesADeterministicComposedIcon"/>'s
    /// single hardcoded reference item into a real curated corpus: every <c>*.icon.json</c> file under
    /// <c>ACE_CLOUD_MULE_ICON_FIXTURE_DIRECTORY</c> is a <see cref="CloudIconGoldenFixture"/> (DID
    /// inputs plus an expected PNG content hash, ASSET-005's "clothing palette/shade variants,
    /// underlays, overlays, tailoring, imbues, magical UI effects, stack counts, and missing/corrupt
    /// references"), composed against the same <c>ACE_CLOUD_MULE_DAT_DIRECTORY</c> operator DAT. Still
    /// reports Inconclusive rather than failing when no corpus is configured, so this never blocks
    /// ordinary CI (issue #24's Red: "do not require private captures to merge this implementation
    /// issue").
    /// </summary>
    [TestMethod]
    public async Task Compare_OperatorOwnedIconFixtureCorpus_EveryFixtureMatches()
    {
        var datDirectory = Environment.GetEnvironmentVariable("ACE_CLOUD_MULE_DAT_DIRECTORY");
        var fixtureDirectory = Environment.GetEnvironmentVariable("ACE_CLOUD_MULE_ICON_FIXTURE_DIRECTORY");

        if (string.IsNullOrWhiteSpace(datDirectory) || string.IsNullOrWhiteSpace(fixtureDirectory))
        {
            Assert.Inconclusive(
                "No local icon fixture corpus is configured. Set ACE_CLOUD_MULE_DAT_DIRECTORY to a directory " +
                "containing an operator-owned client_portal.dat and ACE_CLOUD_MULE_ICON_FIXTURE_DIRECTORY to a " +
                "directory of *.icon.json CloudIconGoldenFixture files to run this golden test.");
            return;
        }

        var sourcePath = Path.Combine(datDirectory, "client_portal.dat");
        if (!File.Exists(sourcePath))
        {
            Assert.Inconclusive($"ACE_CLOUD_MULE_DAT_DIRECTORY is set, but {sourcePath} does not exist.");
            return;
        }

        if (!Directory.Exists(fixtureDirectory) || Directory.GetFiles(fixtureDirectory, "*.icon.json", SearchOption.TopDirectoryOnly).Length == 0)
        {
            Assert.Inconclusive($"No *.icon.json fixture files were found under {fixtureDirectory}.");
            return;
        }

        var fixtures = CloudGoldenFixtureLoader.LoadFromDirectory<CloudIconGoldenFixture>(fixtureDirectory, "*.icon.json");

        var storageRoot = Path.Combine(Path.GetTempPath(), "cloud-icon-golden-corpus-tests", Guid.NewGuid().ToString("N"));
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

            var results = await CloudIconGoldenComparisonHarness.CompareAsync(fixtures, manifestVersion: 1, clothingResolver, layerSource);

            var failures = results.Where(r => !r.Matched)
                .Select(r => $"{r.FixtureName}: {string.Join("; ", r.Differences)}")
                .ToList();

            Assert.HasCount(0, failures, "One or more icon fixtures mismatched:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }
}
