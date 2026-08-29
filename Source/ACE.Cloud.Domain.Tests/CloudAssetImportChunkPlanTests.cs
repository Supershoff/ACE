namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudAssetImportChunkPlanTests
{
    private static CloudAssetChecksum SomeChecksum()
    {
        CloudAssetChecksum.TryParse("a3f1c2d4e5b6a7980123456789abcdef0123456789abcdef0123456789abcd", out var checksum);
        return checksum;
    }

    [TestMethod]
    public void ChunkCount_ExactMultiple_DividesEvenly()
    {
        var plan = new CloudAssetImportChunkPlan(totalBytes: 300, chunkSizeBytes: 100, SomeChecksum());

        Assert.AreEqual(3, plan.ChunkCount);
        Assert.AreEqual(100, plan.ExpectedLengthForChunk(0));
        Assert.AreEqual(100, plan.ExpectedLengthForChunk(2));
    }

    [TestMethod]
    public void ChunkCount_WithARemainder_RoundsUpAndTheLastChunkIsShort()
    {
        var plan = new CloudAssetImportChunkPlan(totalBytes: 250, chunkSizeBytes: 100, SomeChecksum());

        Assert.AreEqual(3, plan.ChunkCount);
        Assert.AreEqual(100, plan.ExpectedLengthForChunk(0));
        Assert.AreEqual(100, plan.ExpectedLengthForChunk(1));
        Assert.AreEqual(50, plan.ExpectedLengthForChunk(2));
    }

    [TestMethod]
    public void ExpectedLengthForChunk_OutOfRange_Throws()
    {
        var plan = new CloudAssetImportChunkPlan(totalBytes: 250, chunkSizeBytes: 100, SomeChecksum());

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => plan.ExpectedLengthForChunk(3));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => plan.ExpectedLengthForChunk(-1));
    }

    [TestMethod]
    public void Constructor_NonPositiveTotalBytes_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudAssetImportChunkPlan(0, 100, SomeChecksum()));
    }

    [TestMethod]
    public void Constructor_NonPositiveChunkSize_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudAssetImportChunkPlan(100, 0, SomeChecksum()));
    }
}
