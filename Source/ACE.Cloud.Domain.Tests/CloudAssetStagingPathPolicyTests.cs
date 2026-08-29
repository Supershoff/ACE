namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// ASSET-004: "Generated public derivatives must not expose the source DAT through path traversal,
/// arbitrary range access, or raw download endpoints." These tests prove every path this policy can
/// ever produce is confined to its own structured segment, and that a hostile shard ID is rejected
/// rather than folded into a path.
/// </summary>
[TestClass]
public sealed class CloudAssetStagingPathPolicyTests
{
    private static readonly Guid SessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ManifestId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public void BuildChunkPartRelativePath_IsScopedUnderTheSessionsDirectory()
    {
        var path = CloudAssetStagingPathPolicy.BuildChunkPartRelativePath(SessionId, chunkIndex: 7);

        Assert.AreEqual($"sessions/{SessionId:N}/chunk-00000007.part", path);
        AssertContainsNoTraversal(path);
    }

    [TestMethod]
    public void BuildAssembledUploadRelativePath_IsScopedUnderTheSessionsDirectory()
    {
        var path = CloudAssetStagingPathPolicy.BuildAssembledUploadRelativePath(SessionId);

        Assert.AreEqual($"sessions/{SessionId:N}/assembled.dat", path);
        AssertContainsNoTraversal(path);
    }

    [TestMethod]
    public void BuildManifestEntryRelativePath_IsScopedUnderTheManifestsDirectory()
    {
        var key = new CloudAssetManifestEntryKey(0x06006C0Au, CloudAssetFileKind.Texture);

        var path = CloudAssetStagingPathPolicy.BuildManifestEntryRelativePath(ManifestId, key);

        Assert.AreEqual($"manifests/{ManifestId:N}/texture/06006c0a.bin", path);
        AssertContainsNoTraversal(path);
    }

    [TestMethod]
    public void BuildRetainedSourceRelativePath_IsScopedUnderTheRetainedDirectory()
    {
        var path = CloudAssetStagingPathPolicy.BuildRetainedSourceRelativePath("us1", CloudAssetKind.Portal);

        Assert.AreEqual("retained/us1/portal.dat", path);
        AssertContainsNoTraversal(path);
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("us1/../../etc")]
    [DataRow("us1\\..\\..\\etc")]
    [DataRow("")]
    [DataRow("has spaces")]
    public void BuildRetainedSourceRelativePath_AnUnsafeShardId_Throws(string shardId)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => CloudAssetStagingPathPolicy.BuildRetainedSourceRelativePath(shardId, CloudAssetKind.Portal));
    }

    [TestMethod]
    public void BuildChunkPartRelativePath_AnEmptySessionId_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudAssetStagingPathPolicy.BuildChunkPartRelativePath(Guid.Empty, 0));
    }

    private static void AssertContainsNoTraversal(string path)
    {
        Assert.IsFalse(path.Contains("..", StringComparison.Ordinal));
        Assert.IsFalse(Path.IsPathRooted(path));
    }
}
