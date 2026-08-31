using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One immutable, versioned Asset Manifest (ASSET-002, ASSET-004): the complete, DID-addressable
/// result of one successful staging/extraction pass. A manifest is created only once, already
/// <see cref="CloudAssetManifestState.StagingComplete"/> and carrying its final
/// <see cref="EntryCount"/> and entry rows; it is never mutated afterward except for the
/// <see cref="State"/>/<see cref="ActivatedAtUtc"/>/<see cref="SupersededAtUtc"/> transitions
/// <see cref="CloudAssetImportBoundary.ActivateManifestAsync"/> performs.
/// </summary>
public sealed class CloudAssetManifest
{
    private CloudAssetManifest()
    {
    }

    public CloudAssetManifest(Guid id, string shardId, CloudAssetKind kind, int version, Guid sourceImportSessionId, int entryCount)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An Asset Manifest requires a real ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An Asset Manifest requires a Cloud Shard ID.", nameof(shardId));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (sourceImportSessionId == Guid.Empty)
        {
            throw new ArgumentException("An Asset Manifest requires its source import session ID.", nameof(sourceImportSessionId));
        }

        if (entryCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entryCount), "An Asset Manifest requires at least one entry.");
        }

        Id = id;
        ShardId = shardId;
        Kind = kind;
        Version = version;
        SourceImportSessionId = sourceImportSessionId;
        EntryCount = entryCount;
        State = CloudAssetManifestState.StagingComplete;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudAssetKind Kind { get; private set; }

    public int Version { get; private set; }

    public CloudAssetManifestState State { get; private set; }

    public Guid SourceImportSessionId { get; private set; }

    public int EntryCount { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ActivatedAtUtc { get; private set; }

    public DateTime? SupersededAtUtc { get; private set; }

    public void MarkActive(DateTime activatedAtUtc)
    {
        State = CloudAssetManifestState.Active;
        ActivatedAtUtc = activatedAtUtc;
    }

    public void MarkSuperseded(DateTime supersededAtUtc)
    {
        State = CloudAssetManifestState.Superseded;
        SupersededAtUtc = supersededAtUtc;
    }
}
