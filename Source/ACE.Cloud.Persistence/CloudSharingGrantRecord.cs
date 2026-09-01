using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's own persisted Sharing Grant row (issue #36, SHARE-001..004):
/// one owner's current permission assignment to one resolved grantee ownership group, keyed
/// uniquely by (<see cref="ShardId"/>, <see cref="OwnerId"/>, <see cref="GranteeId"/>) so "set" is
/// always an idempotent upsert rather than an ever-growing history table -- <see cref="Level"/>
/// including <see cref="CloudSharingGrantLevel.None"/> is itself the current, real, auditable state
/// (SHARE-004: "None is an explicit denial"), not a deletion. <see cref="GranteeId"/> is resolved
/// exactly once at set time from the owner's typed current character name and never re-resolved
/// (SHARE-001).
/// </summary>
public sealed class CloudSharingGrantRecord
{
    private CloudSharingGrantRecord()
    {
    }

    private CloudSharingGrantRecord(
        Guid id,
        string shardId,
        Guid ownerId,
        Guid granteeId,
        CloudSharingGrantLevel level,
        int version,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        Id = id;
        ShardId = shardId;
        OwnerId = ownerId;
        GranteeId = granteeId;
        Level = level;
        Version = version;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public static CloudSharingGrantRecord Open(
        Guid id, string shardId, Guid ownerId, Guid granteeId, CloudSharingGrantLevel level, DateTime createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A Sharing Grant requires a real ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Sharing Grant requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A Sharing Grant requires an owner.", nameof(ownerId));
        }

        if (granteeId == Guid.Empty)
        {
            throw new ArgumentException("A Sharing Grant requires a grantee.", nameof(granteeId));
        }

        if (granteeId == ownerId)
        {
            throw new ArgumentException("A Sharing Grant cannot name its owner as its own grantee.", nameof(granteeId));
        }

        return new CloudSharingGrantRecord(id, shardId, ownerId, granteeId, level, version: 1, createdAtUtc, createdAtUtc);
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public Guid OwnerId { get; private set; }

    public Guid GranteeId { get; private set; }

    public CloudSharingGrantLevel Level { get; private set; }

    /// <summary>Optimistic concurrency token (ARCH-006), bumped only on a real value change (mirrors <see cref="CloudSharingGrant.WithLevel"/>).</summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Applies a new level. Callers must already hold this row's lock for the whole boundary
    /// transaction (mirrors <c>CloudTransferOfferRecord.Resolve</c>'s established rationale: not
    /// literally reused because <see cref="CloudSharingGrant"/>'s own transition is internal to
    /// ACE.Cloud.Domain and this persisted row already carries its own authoritative version).
    /// Returns false for a same-value re-send, which is a deliberate no-op that does not bump
    /// <see cref="Version"/> or <see cref="UpdatedAtUtc"/>.
    /// </summary>
    internal bool TrySetLevel(CloudSharingGrantLevel level, DateTime updatedAtUtc)
    {
        if (Level == level)
        {
            return false;
        }

        Level = level;
        Version++;
        UpdatedAtUtc = updatedAtUtc;
        return true;
    }
}
