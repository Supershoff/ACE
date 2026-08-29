namespace ACE.Cloud.Domain;

/// <summary>
/// The declared shape of one resumable upload, fixed at session creation and immutable for its
/// whole lifetime (ASSET-002). <see cref="ChunkCount"/> is derived, never independently supplied, so
/// a caller cannot desynchronize it from <see cref="TotalBytes"/>/<see cref="ChunkSizeBytes"/>.
/// </summary>
public sealed record CloudAssetImportChunkPlan
{
    public long TotalBytes { get; }

    public int ChunkSizeBytes { get; }

    public int ChunkCount { get; }

    public CloudAssetChecksum ExpectedChecksum { get; }

    public CloudAssetImportChunkPlan(long totalBytes, int chunkSizeBytes, CloudAssetChecksum expectedChecksum)
    {
        if (totalBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes), "An import requires a positive declared byte length.");
        }

        if (chunkSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes), "An import requires a positive chunk size.");
        }

        TotalBytes = totalBytes;
        ChunkSizeBytes = chunkSizeBytes;
        ExpectedChecksum = expectedChecksum;
        ChunkCount = checked((int)((totalBytes + chunkSizeBytes - 1) / chunkSizeBytes));
    }

    /// <summary>
    /// The exact byte length the chunk at <paramref name="chunkIndex"/> must have: every chunk is
    /// <see cref="ChunkSizeBytes"/> except the final one, which is whatever remains.
    /// </summary>
    public long ExpectedLengthForChunk(int chunkIndex)
    {
        if (chunkIndex < 0 || chunkIndex >= ChunkCount)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        var isLastChunk = chunkIndex == ChunkCount - 1;
        if (!isLastChunk)
        {
            return ChunkSizeBytes;
        }

        var remainder = TotalBytes - (long)ChunkSizeBytes * (ChunkCount - 1);
        return remainder;
    }
}
