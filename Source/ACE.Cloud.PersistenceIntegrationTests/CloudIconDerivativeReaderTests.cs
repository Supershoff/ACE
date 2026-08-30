using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #31 Red/Green coverage for serving an already-composed icon derivative back to the web
/// client by its persisted <see cref="CloudInventoryQueryResultItem.IconCacheKeyHex"/> alone. Exercised
/// entirely against a temp-directory <see cref="LocalProtectedAssetBlobStore"/> -- no MariaDB fixture
/// required, since this reader never touches the database.
/// </summary>
[TestClass]
public sealed class CloudIconDerivativeReaderTests
{
    [TestMethod]
    public async Task TryReadAsync_ComposedDerivativeExists_ReturnsItsBytes()
    {
        var storageRoot = CreateTempStorageRoot();
        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var cacheKey = CloudIconCompositionCacheKey.FromHex(new string('a', 64));
            var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            await blobStore.WriteAsync(CloudAssetStagingPathPolicy.BuildIconCompositionCacheRelativePath(cacheKey), pngBytes);

            var reader = new CloudIconDerivativeReader(blobStore);
            var result = await reader.TryReadAsync(cacheKey);

            CollectionAssert.AreEqual(pngBytes, result);
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task TryReadAsync_NoDerivativeComposedYet_ReturnsNullRatherThanThrowing()
    {
        var storageRoot = CreateTempStorageRoot();
        try
        {
            var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
            var reader = new CloudIconDerivativeReader(blobStore);

            var result = await reader.TryReadAsync(CloudIconCompositionCacheKey.FromHex(new string('b', 64)));

            Assert.IsNull(result);
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    private static string CreateTempStorageRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "ace-cloud-icon-derivative-reader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
