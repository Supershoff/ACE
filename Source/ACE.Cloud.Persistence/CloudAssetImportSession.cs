using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One resumable Asset Import attempt (ASSET-002). <see cref="TotalBytes"/>,
/// <see cref="ChunkSizeBytes"/>, <see cref="ChunkCount"/>, and <see cref="ExpectedChecksumHex"/> are
/// the session's immutable declared plan, fixed at creation; only <see cref="State"/>,
/// <see cref="ReceivedChunkCount"/>, <see cref="ManifestId"/>, and <see cref="ErrorMessage"/> change
/// afterward. <see cref="CloudAssetImportBoundary"/> owns every transition; this type is a plain
/// persisted record, not itself a policy.
/// </summary>
public sealed class CloudAssetImportSession
{
    private CloudAssetImportSession()
    {
    }

    public CloudAssetImportSession(
        Guid id,
        string shardId,
        CloudAssetKind kind,
        long totalBytes,
        int chunkSizeBytes,
        int chunkCount,
        string expectedChecksumHex,
        uint initiatedByAccountId)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An Asset Import session requires a real ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An Asset Import session requires a Cloud Shard ID.", nameof(shardId));
        }

        if (totalBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes));
        }

        if (chunkSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSizeBytes));
        }

        if (chunkCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkCount));
        }

        if (string.IsNullOrWhiteSpace(expectedChecksumHex))
        {
            throw new ArgumentException("An Asset Import session requires its declared checksum.", nameof(expectedChecksumHex));
        }

        if (initiatedByAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initiatedByAccountId), "An Asset Import session requires a real administrator account ID.");
        }

        Id = id;
        ShardId = shardId;
        Kind = kind;
        TotalBytes = totalBytes;
        ChunkSizeBytes = chunkSizeBytes;
        ChunkCount = chunkCount;
        ExpectedChecksumHex = expectedChecksumHex;
        InitiatedByAccountId = initiatedByAccountId;
        State = CloudAssetImportSessionState.Uploading;
        ReceivedChunkCount = 0;
        Version = 1;
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudAssetKind Kind { get; private set; }

    public long TotalBytes { get; private set; }

    public int ChunkSizeBytes { get; private set; }

    public int ChunkCount { get; private set; }

    public string ExpectedChecksumHex { get; private set; } = null!;

    public uint InitiatedByAccountId { get; private set; }

    public CloudAssetImportSessionState State { get; private set; }

    public int ReceivedChunkCount { get; private set; }

    /// <summary>Set once staging produces a manifest (<see cref="CloudAssetImportSessionState.StagingComplete"/>).</summary>
    public Guid? ManifestId { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public CloudAssetImportChunkPlan ToChunkPlan()
    {
        CloudAssetChecksum.TryParse(ExpectedChecksumHex, out var checksum);
        return new CloudAssetImportChunkPlan(TotalBytes, ChunkSizeBytes, checksum);
    }

    public void RecordAcceptedChunk()
    {
        ReceivedChunkCount++;
        Version++;
    }

    /// <summary>Uploading -> Staging: the checksum-verified upload is queued for background extraction.</summary>
    public void MarkQueuedForStaging()
    {
        State = CloudAssetImportSessionState.Staging;
        Version++;
    }

    public void MarkChecksumFailed(string reason)
    {
        State = CloudAssetImportSessionState.ChecksumFailed;
        ErrorMessage = reason;
        Version++;
    }

    public void MarkStagingComplete(Guid manifestId)
    {
        State = CloudAssetImportSessionState.StagingComplete;
        ManifestId = manifestId;
        Version++;
    }

    public void MarkStagingFailed(string reason)
    {
        State = CloudAssetImportSessionState.StagingFailed;
        ErrorMessage = reason;
        Version++;
    }

    public void MarkCancelled(string reason)
    {
        State = CloudAssetImportSessionState.Cancelled;
        ErrorMessage = reason;
        Version++;
    }

    /// <summary>
    /// Builds a session that starts directly in <see cref="CloudAssetImportSessionState.Staging"/>,
    /// bypassing chunk upload entirely, for reprocessing an already-retained source DAT
    /// (ASSET-003). Its plan fields describe the retained file as a single "chunk" purely so
    /// <see cref="ToChunkPlan"/> stays well-formed; nothing re-reads them once staging begins.
    /// </summary>
    public static CloudAssetImportSession CreateForReprocessing(
        Guid id, string shardId, CloudAssetKind kind, long totalBytes, string checksumHex, uint initiatedByAccountId)
    {
        var chunkSize = totalBytes > int.MaxValue ? int.MaxValue : (int)totalBytes;
        var session = new CloudAssetImportSession(id, shardId, kind, totalBytes, chunkSize, chunkCount: 1, checksumHex, initiatedByAccountId);
        session.ReceivedChunkCount = 1;
        session.State = CloudAssetImportSessionState.Staging;
        session.Version = 1;
        return session;
    }
}
