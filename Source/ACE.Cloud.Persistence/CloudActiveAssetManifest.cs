using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single pointer, keyed by (<see cref="ShardId"/>, <see cref="Kind"/>), to whichever
/// <see cref="CloudAssetManifest"/> is currently active (ASSET-002: "atomically replaces the active
/// asset manifest only after success"). Exists as its own tiny table for the same reason
/// <see cref="CloudActiveAccountLinkMarker"/> does: a MariaDB unique index cannot be scoped to "only
/// rows where State = Active" while <see cref="CloudAssetManifest"/> also retains every historical
/// (Superseded) version. Locking this row is what makes
/// <see cref="CloudAssetImportBoundary.ActivateManifestAsync"/>'s read-decide-swap atomic against a
/// concurrent activation attempt for the same shard/kind (ASSET-002's Red test: "activation race").
/// </summary>
public sealed class CloudActiveAssetManifest
{
    private CloudActiveAssetManifest()
    {
    }

    public CloudActiveAssetManifest(string shardId, CloudAssetKind kind, Guid manifestId, int manifestVersion)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An active manifest pointer requires a Cloud Shard ID.", nameof(shardId));
        }

        if (manifestId == Guid.Empty)
        {
            throw new ArgumentException("An active manifest pointer requires its manifest ID.", nameof(manifestId));
        }

        if (manifestVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(manifestVersion));
        }

        ShardId = shardId;
        Kind = kind;
        ManifestId = manifestId;
        ManifestVersion = manifestVersion;
    }

    public string ShardId { get; private set; } = null!;

    public CloudAssetKind Kind { get; private set; }

    public Guid ManifestId { get; private set; }

    public int ManifestVersion { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public void PointTo(Guid manifestId, int manifestVersion)
    {
        if (manifestId == Guid.Empty)
        {
            throw new ArgumentException("An active manifest pointer requires its manifest ID.", nameof(manifestId));
        }

        if (manifestVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(manifestVersion));
        }

        ManifestId = manifestId;
        ManifestVersion = manifestVersion;
    }
}
