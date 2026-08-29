namespace ACE.Cloud.Domain;

/// <summary>
/// Pure decisions for resumable Asset Import uploads (ASSET-002): whether one uploaded chunk may be
/// accepted, ignored as an idempotent duplicate, or rejected, and whether a session's received
/// chunks are ready to finalize into a checksum-verified upload. Every method here is a pure
/// function over its inputs; callers own actually persisting chunk bytes/metadata and looking up
/// what has already been recorded (<see cref="CloudCustodianConfigurationPolicy"/>'s doc comment
/// explains why this split exists: the same validation must run identically in a unit test and
/// behind the locked persistence boundary).
/// </summary>
public static class CloudAssetImportUploadPolicy
{
    public static CloudAssetImportChunkDecision EvaluateChunk(
        CloudAssetImportSessionState currentState,
        CloudAssetImportChunkPlan plan,
        int chunkIndex,
        long chunkByteLength,
        CloudAssetChecksum chunkChecksum,
        CloudAssetChecksum? previouslyRecordedChecksum)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (currentState != CloudAssetImportSessionState.Uploading)
        {
            return CloudAssetImportChunkDecision.Rejected(
                $"The session is in state {currentState} and cannot accept chunk uploads.");
        }

        if (chunkIndex < 0 || chunkIndex >= plan.ChunkCount)
        {
            return CloudAssetImportChunkDecision.Rejected(
                $"Chunk index {chunkIndex} is out of range for a {plan.ChunkCount}-chunk upload.");
        }

        var expectedLength = plan.ExpectedLengthForChunk(chunkIndex);
        if (chunkByteLength != expectedLength)
        {
            return CloudAssetImportChunkDecision.Rejected(
                $"Chunk {chunkIndex} has {chunkByteLength} bytes but the declared plan requires exactly {expectedLength}.");
        }

        if (previouslyRecordedChecksum is { } existing)
        {
            return existing.Equals(chunkChecksum)
                ? CloudAssetImportChunkDecision.DuplicateIgnored()
                : CloudAssetImportChunkDecision.Rejected(
                    $"Chunk {chunkIndex} was already received with a different checksum; this looks like corrupt or conflicting resend data.");
        }

        return CloudAssetImportChunkDecision.Accepted();
    }

    public static CloudAssetUploadFinalizationDecision EvaluateFinalization(
        CloudAssetImportSessionState currentState,
        CloudAssetImportChunkPlan plan,
        int receivedChunkCount,
        CloudAssetChecksum computedChecksum)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (currentState != CloudAssetImportSessionState.Uploading)
        {
            return CloudAssetUploadFinalizationDecision.InvalidState(
                $"The session is in state {currentState} and cannot be finalized.");
        }

        if (receivedChunkCount < plan.ChunkCount)
        {
            return CloudAssetUploadFinalizationDecision.Incomplete(
                $"Received {receivedChunkCount} of {plan.ChunkCount} required chunks.");
        }

        if (!computedChecksum.Equals(plan.ExpectedChecksum))
        {
            return CloudAssetUploadFinalizationDecision.ChecksumMismatch(
                "The assembled upload does not match its declared checksum.");
        }

        return CloudAssetUploadFinalizationDecision.Completed();
    }
}
