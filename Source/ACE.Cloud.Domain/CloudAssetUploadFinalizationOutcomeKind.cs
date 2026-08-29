namespace ACE.Cloud.Domain;

public enum CloudAssetUploadFinalizationOutcomeKind
{
    /// <summary>Every declared chunk is present and the assembled bytes match the declared checksum.</summary>
    Completed,

    /// <summary>Fewer than <see cref="CloudAssetImportChunkPlan.ChunkCount"/> chunks have been received yet.</summary>
    Incomplete,

    /// <summary>All chunks are present, but the assembled bytes do not match the declared checksum (ASSET-002: "wrong format/checksum").</summary>
    ChecksumMismatch,

    /// <summary>The session is not in a state finalization can be attempted from.</summary>
    InvalidState,
}
