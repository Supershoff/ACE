using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Cloud.Worker;

namespace ACE.Cloud.Worker.Tests;

/// <summary>
/// Protected golden verification for <see cref="PortalDatAssetExtractor"/> (ASSET-002, ASSET-004),
/// following the same pattern issue #24 established: this test requires an operator-owned
/// <c>client_portal.dat</c> that is never committed to the repository (repository policy forbids
/// it), so it is inconclusive rather than failing when no corpus is configured -- exactly the
/// "record the expected... missing-corpus report" the Red section asks for. CI has no DAT
/// available and is expected to see this test report Inconclusive; the
/// <c>CloudAssetImportBoundaryTests</c> integration suite covers the staging pipeline's state
/// machine end to end using synthetic entries instead (Refactor: "keep synthetic CI coverage
/// distinct from protected golden verification").
///
/// To run this for real, set the <c>ACE_CLOUD_MULE_DAT_DIRECTORY</c> environment variable to a
/// local directory containing a standard <c>client_portal.dat</c> before running the test.
/// </summary>
[TestClass]
public sealed class PortalDatAssetExtractorTests
{
    // The same reference item issue #24 validated: TreeStats WCID 42635's base icon and overlay.
    private const uint KnownBaseIconDid = 0x06006C0A;
    private const uint KnownOverlayDid = 0x06006C34;

    [TestMethod]
    public async Task ExtractAsync_AnOperatorOwnedPortalDat_ProducesADidAddressableTextureManifest()
    {
        var datDirectory = Environment.GetEnvironmentVariable("ACE_CLOUD_MULE_DAT_DIRECTORY");
        if (string.IsNullOrWhiteSpace(datDirectory))
        {
            Assert.Inconclusive(
                "No local client_portal.dat is configured. Set ACE_CLOUD_MULE_DAT_DIRECTORY to a " +
                "directory containing an operator-owned client_portal.dat to run this golden test.");
            return;
        }

        var sourcePath = Path.Combine(datDirectory, "client_portal.dat");
        if (!File.Exists(sourcePath))
        {
            Assert.Inconclusive($"ACE_CLOUD_MULE_DAT_DIRECTORY is set, but {sourcePath} does not exist.");
            return;
        }

        var storageRoot = Path.Combine(Path.GetTempPath(), "cloud-asset-extractor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageRoot);

        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var extractor = new PortalDatAssetExtractor();
            var manifestId = Guid.NewGuid();

            var entries = await extractor.ExtractAsync(sourcePath, manifestId, blobStore);

            Assert.IsGreaterThan(0, entries.Count, "Expected at least one extracted texture entry.");
            Assert.IsTrue(entries.All(e => e.Key.Kind == CloudAssetFileKind.Texture));

            var baseIcon = entries.SingleOrDefault(e => e.Key.Did == KnownBaseIconDid);
            var overlay = entries.SingleOrDefault(e => e.Key.Did == KnownOverlayDid);
            Assert.IsNotNull(baseIcon, "Expected the known TreeStats base icon DID to be extracted.");
            Assert.IsNotNull(overlay, "Expected the known TreeStats overlay DID to be extracted.");

            foreach (var entry in entries)
            {
                var exists = await blobStore.ExistsAsync(entry.RelativePath);
                Assert.IsTrue(exists, $"Expected staged bytes at {entry.RelativePath} for DID {entry.Key.DidHex}.");
            }
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }
}
