using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single pointer, keyed by (<see cref="ShardId"/>, <see cref="Kind"/>), to the most recently
/// created Asset Import session for that key -- in-flight or terminal. Locking this row (the same
/// "lock the not-yet-existing row's gap, then insert" technique
/// <c>CloudAccountLinkGateway.LockActiveLinkMarkerAsync</c> uses) is what actually serializes two
/// concurrent "start a new import" requests for the same shard/kind (ASSET-002's Red test:
/// "concurrent imports") into a create-then-resume pair instead of two independent sessions racing
/// each other.
/// </summary>
public sealed class CloudAssetImportCurrentSessionMarker
{
    private CloudAssetImportCurrentSessionMarker()
    {
    }

    public CloudAssetImportCurrentSessionMarker(string shardId, CloudAssetKind kind, Guid sessionId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An Asset Import session marker requires a Cloud Shard ID.", nameof(shardId));
        }

        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("An Asset Import session marker requires its session ID.", nameof(sessionId));
        }

        ShardId = shardId;
        Kind = kind;
        SessionId = sessionId;
    }

    public string ShardId { get; private set; } = null!;

    public CloudAssetKind Kind { get; private set; }

    public Guid SessionId { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void PointTo(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            throw new ArgumentException("An Asset Import session marker requires its session ID.", nameof(sessionId));
        }

        SessionId = sessionId;
    }
}
