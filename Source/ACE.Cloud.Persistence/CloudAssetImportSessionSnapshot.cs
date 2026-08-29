using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>A read-only projection of one <see cref="CloudAssetImportSession"/>, returned by every <see cref="CloudAssetImportBoundary"/> call.</summary>
public sealed record CloudAssetImportSessionSnapshot(
    Guid Id,
    string ShardId,
    CloudAssetKind Kind,
    CloudAssetImportSessionState State,
    long TotalBytes,
    int ChunkSizeBytes,
    int ChunkCount,
    int ReceivedChunkCount,
    Guid? ManifestId,
    string? ErrorMessage,
    int Version,
    bool WasResumed)
{
    public static CloudAssetImportSessionSnapshot From(CloudAssetImportSession session, bool wasResumed = false) => new(
        session.Id, session.ShardId, session.Kind, session.State, session.TotalBytes, session.ChunkSizeBytes,
        session.ChunkCount, session.ReceivedChunkCount, session.ManifestId, session.ErrorMessage, session.Version, wasResumed);
}
