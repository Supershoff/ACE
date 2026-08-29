namespace ACE.Cloud.Persistence;

/// <summary>
/// One durably recorded, accepted upload chunk (ASSET-002). Its presence (keyed by
/// (<see cref="SessionId"/>, <see cref="ChunkIndex"/>)) is what makes a resend of the same chunk
/// detectable as a duplicate rather than re-validated from scratch every time
/// (<see cref="ACE.Cloud.Domain.CloudAssetImportUploadPolicy.EvaluateChunk"/>).
/// </summary>
public sealed class CloudAssetImportChunkRecord
{
    private CloudAssetImportChunkRecord()
    {
    }

    public CloudAssetImportChunkRecord(Guid sessionId, int chunkIndex, string sha256Hex, long byteLength)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("A chunk record requires its session ID.", nameof(sessionId));
        }

        if (chunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        if (string.IsNullOrWhiteSpace(sha256Hex))
        {
            throw new ArgumentException("A chunk record requires its checksum.", nameof(sha256Hex));
        }

        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        SessionId = sessionId;
        ChunkIndex = chunkIndex;
        Sha256Hex = sha256Hex;
        ByteLength = byteLength;
    }

    public Guid SessionId { get; private set; }

    public int ChunkIndex { get; private set; }

    public string Sha256Hex { get; private set; } = null!;

    public long ByteLength { get; private set; }

    public DateTime ReceivedAtUtc { get; private set; }
}
