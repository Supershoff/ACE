using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single latest successfully checksum-verified source DAT retained privately per shard/kind
/// (ASSET-003: "Retain the latest uploaded source DAT in protected non-public storage for automatic
/// reprocessing by future Cloud Mule versions"). Overwritten -- never appended -- by every
/// subsequent successful upload; <see cref="CloudAssetImportBoundary.ReprocessLatestRetainedAsync"/>
/// is the only reader that ever needs its bytes. No public endpoint exposes
/// <see cref="RelativePath"/> or reads its bytes (ASSET-004).
/// </summary>
public sealed class CloudRetainedSourceAsset
{
    private CloudRetainedSourceAsset()
    {
    }

    public CloudRetainedSourceAsset(string shardId, CloudAssetKind kind, string relativePath, long byteLength, string sha256Hex, Guid sourceImportSessionId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A retained source asset requires a Cloud Shard ID.", nameof(shardId));
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A retained source asset requires its stored relative path.", nameof(relativePath));
        }

        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        if (string.IsNullOrWhiteSpace(sha256Hex))
        {
            throw new ArgumentException("A retained source asset requires its checksum.", nameof(sha256Hex));
        }

        if (sourceImportSessionId == Guid.Empty)
        {
            throw new ArgumentException("A retained source asset requires its source import session ID.", nameof(sourceImportSessionId));
        }

        ShardId = shardId;
        Kind = kind;
        RelativePath = relativePath;
        ByteLength = byteLength;
        Sha256Hex = sha256Hex;
        SourceImportSessionId = sourceImportSessionId;
    }

    public string ShardId { get; private set; } = null!;

    public CloudAssetKind Kind { get; private set; }

    public string RelativePath { get; private set; } = null!;

    public long ByteLength { get; private set; }

    public string Sha256Hex { get; private set; } = null!;

    public Guid SourceImportSessionId { get; private set; }

    public DateTime RetainedAtUtc { get; private set; }

    public void ReplaceWith(string relativePath, long byteLength, string sha256Hex, Guid sourceImportSessionId)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A retained source asset requires its stored relative path.", nameof(relativePath));
        }

        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        if (string.IsNullOrWhiteSpace(sha256Hex))
        {
            throw new ArgumentException("A retained source asset requires its checksum.", nameof(sha256Hex));
        }

        if (sourceImportSessionId == Guid.Empty)
        {
            throw new ArgumentException("A retained source asset requires its source import session ID.", nameof(sourceImportSessionId));
        }

        RelativePath = relativePath;
        ByteLength = byteLength;
        Sha256Hex = sha256Hex;
        SourceImportSessionId = sourceImportSessionId;
    }
}
