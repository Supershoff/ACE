namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green coverage for issue #25's resumable chunk-upload rules (ASSET-002's Red tests:
/// "malformed/truncated input, wrong format/checksum, interrupted/resumed upload, duplicate
/// chunks... worker crash").
/// </summary>
[TestClass]
public sealed class CloudAssetImportUploadPolicyTests
{
    private static CloudAssetChecksum Checksum(string suffix = "ef") =>
        Parse($"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcd{suffix}");

    private static CloudAssetChecksum Parse(string raw)
    {
        CloudAssetChecksum.TryParse(raw, out var checksum);
        return checksum;
    }

    private static CloudAssetImportChunkPlan Plan(long totalBytes = 250, int chunkSize = 100) =>
        new(totalBytes, chunkSize, Checksum());

    [TestMethod]
    public void EvaluateChunk_ANewInRangeChunkWithTheDeclaredLength_IsAccepted()
    {
        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(
            CloudAssetImportSessionState.Uploading, Plan(), chunkIndex: 0, chunkByteLength: 100, Checksum(), previouslyRecordedChecksum: null);

        Assert.AreEqual(CloudAssetImportChunkOutcomeKind.Accepted, decision.Kind);
    }

    [TestMethod]
    public void EvaluateChunk_TheFinalShortChunkWithItsExactRemainderLength_IsAccepted()
    {
        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(
            CloudAssetImportSessionState.Uploading, Plan(), chunkIndex: 2, chunkByteLength: 50, Checksum(), previouslyRecordedChecksum: null);

        Assert.AreEqual(CloudAssetImportChunkOutcomeKind.Accepted, decision.Kind);
    }

    [TestMethod]
    public void EvaluateChunk_SessionNotUploading_IsRejected()
    {
        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(
            CloudAssetImportSessionState.Staging, Plan(), chunkIndex: 0, chunkByteLength: 100, Checksum(), previouslyRecordedChecksum: null);

        Assert.AreEqual(CloudAssetImportChunkOutcomeKind.Rejected, decision.Kind);
    }

    [TestMethod]
    public void EvaluateChunk_IndexAtOrAboveChunkCount_IsRejected()
    {
        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(
            CloudAssetImportSessionState.Uploading, Plan(), chunkIndex: 3, chunkByteLength: 50, Checksum(), previouslyRecordedChecksum: null);

        Assert.AreEqual(CloudAssetImportChunkOutcomeKind.Rejected, decision.Kind);
    }

    [TestMethod]
    public void EvaluateChunk_NegativeIndex_IsRejected()
    {
        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(
            CloudAssetImportSessionState.Uploading, Plan(), chunkIndex: -1, chunkByteLength: 100, Checksum(), previouslyRecordedChecksum: null);

        Assert.AreEqual(CloudAssetImportChunkOutcomeKind.Rejected, decision.Kind);
    }

    [TestMethod]
    public void EvaluateChunk_WrongByteLength_IsRejected()
    {
        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(
            CloudAssetImportSessionState.Uploading, Plan(), chunkIndex: 0, chunkByteLength: 99, Checksum(), previouslyRecordedChecksum: null);

        Assert.AreEqual(CloudAssetImportChunkOutcomeKind.Rejected, decision.Kind);
    }

    [TestMethod]
    public void EvaluateChunk_TruncatedFinalChunk_IsRejected()
    {
        // ASSET-002 Red test: "malformed/truncated input" -- the last chunk claims fewer bytes
        // than the declared plan's exact remainder.
        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(
            CloudAssetImportSessionState.Uploading, Plan(), chunkIndex: 2, chunkByteLength: 10, Checksum(), previouslyRecordedChecksum: null);

        Assert.AreEqual(CloudAssetImportChunkOutcomeKind.Rejected, decision.Kind);
    }

    [TestMethod]
    public void EvaluateChunk_AResendOfAnIdenticalChunk_IsIgnoredAsADuplicate()
    {
        // ASSET-002 Red tests: "interrupted/resumed upload" and "duplicate chunks" -- a resume
        // retries a chunk whose acknowledgement was lost; the resend must be a safe no-op.
        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(
            CloudAssetImportSessionState.Uploading, Plan(), chunkIndex: 0, chunkByteLength: 100, Checksum(), previouslyRecordedChecksum: Checksum());

        Assert.AreEqual(CloudAssetImportChunkOutcomeKind.DuplicateIgnored, decision.Kind);
    }

    [TestMethod]
    public void EvaluateChunk_AConflictingResendOfTheSameIndex_IsRejected()
    {
        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(
            CloudAssetImportSessionState.Uploading, Plan(), chunkIndex: 0, chunkByteLength: 100, Checksum("00"), previouslyRecordedChecksum: Checksum("cd"));

        Assert.AreEqual(CloudAssetImportChunkOutcomeKind.Rejected, decision.Kind);
    }

    [TestMethod]
    public void EvaluateFinalization_AllChunksReceivedAndChecksumMatches_Completes()
    {
        var decision = CloudAssetImportUploadPolicy.EvaluateFinalization(
            CloudAssetImportSessionState.Uploading, Plan(), receivedChunkCount: 3, computedChecksum: Checksum());

        Assert.IsTrue(decision.IsCompleted);
    }

    [TestMethod]
    public void EvaluateFinalization_FewerChunksThanDeclared_IsIncomplete()
    {
        var decision = CloudAssetImportUploadPolicy.EvaluateFinalization(
            CloudAssetImportSessionState.Uploading, Plan(), receivedChunkCount: 2, computedChecksum: Checksum());

        Assert.AreEqual(CloudAssetUploadFinalizationOutcomeKind.Incomplete, decision.Kind);
    }

    [TestMethod]
    public void EvaluateFinalization_AllChunksButWrongChecksum_IsAChecksumMismatch()
    {
        // ASSET-002 Red test: "wrong format/checksum".
        var decision = CloudAssetImportUploadPolicy.EvaluateFinalization(
            CloudAssetImportSessionState.Uploading, Plan(), receivedChunkCount: 3, computedChecksum: Checksum("00"));

        Assert.AreEqual(CloudAssetUploadFinalizationOutcomeKind.ChecksumMismatch, decision.Kind);
    }

    [TestMethod]
    public void EvaluateFinalization_SessionNotUploading_IsInvalidState()
    {
        // Covers a "worker crash"/reprocessing style attempt to finalize an already-resolved
        // session a second time.
        var decision = CloudAssetImportUploadPolicy.EvaluateFinalization(
            CloudAssetImportSessionState.StagingComplete, Plan(), receivedChunkCount: 3, computedChecksum: Checksum());

        Assert.AreEqual(CloudAssetUploadFinalizationOutcomeKind.InvalidState, decision.Kind);
    }
}
