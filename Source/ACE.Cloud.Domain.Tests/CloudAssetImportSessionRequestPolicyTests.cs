namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudAssetImportSessionRequestPolicyTests
{
    private const long MaxTotalBytes = 10_000;
    private const int MaxChunkSizeBytes = 1_000;

    [TestMethod]
    public void Evaluate_WithinBothLimits_IsValid()
    {
        var decision = CloudAssetImportSessionRequestPolicy.Evaluate(5_000, 500, MaxTotalBytes, MaxChunkSizeBytes);

        Assert.IsTrue(decision.IsValid);
    }

    [TestMethod]
    public void Evaluate_TotalBytesExceedsTheConfiguredMaximum_IsInvalid()
    {
        var decision = CloudAssetImportSessionRequestPolicy.Evaluate(20_000, 500, MaxTotalBytes, MaxChunkSizeBytes);

        Assert.IsFalse(decision.IsValid);
    }

    [TestMethod]
    public void Evaluate_ChunkSizeExceedsTheConfiguredMaximum_IsInvalid()
    {
        var decision = CloudAssetImportSessionRequestPolicy.Evaluate(5_000, 5_000, MaxTotalBytes, MaxChunkSizeBytes);

        Assert.IsFalse(decision.IsValid);
    }

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public void Evaluate_NonPositiveTotalBytes_IsInvalid(long totalBytes)
    {
        var decision = CloudAssetImportSessionRequestPolicy.Evaluate(totalBytes, 500, MaxTotalBytes, MaxChunkSizeBytes);

        Assert.IsFalse(decision.IsValid);
    }
}
