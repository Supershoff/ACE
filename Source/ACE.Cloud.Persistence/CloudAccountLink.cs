namespace ACE.Cloud.Persistence;

/// <summary>
/// One account's membership in a <see cref="CloudOwnershipGroup"/> (AUTH-005, AUTH-006): while
/// <see cref="Status"/> is <see cref="CloudAccountLinkStatus.Active"/>, this account's future
/// deposits route to the group's Main Account. Unlinking never deletes this row -- it becomes
/// <see cref="CloudAccountLinkStatus.Unlinked"/> instead -- so link/unlink history remains available
/// for audit exactly like every other Activity Ledger-adjacent record in this schema.
/// </summary>
public sealed class CloudAccountLink
{
    private CloudAccountLink()
    {
    }

    public static CloudAccountLink Open(Guid ownershipGroupId, string shardId, uint linkedAccountId, DateTime nowUtc)
    {
        if (ownershipGroupId == Guid.Empty)
        {
            throw new ArgumentException("An account link requires its ownership group ID.", nameof(ownershipGroupId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An account link requires a Cloud Shard ID.", nameof(shardId));
        }

        if (linkedAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(linkedAccountId), "An account link requires a real linked account ID.");
        }

        return new CloudAccountLink
        {
            Id = Guid.NewGuid(),
            OwnershipGroupId = ownershipGroupId,
            ShardId = shardId,
            LinkedAccountId = linkedAccountId,
            Status = CloudAccountLinkStatus.Active,
            LinkedAtUtc = nowUtc,
        };
    }

    public Guid Id { get; private set; }

    public Guid OwnershipGroupId { get; private set; }

    public string ShardId { get; private set; } = null!;

    /// <summary>The immutable ACE account ID whose future deposits this row currently routes (AUTH-005).</summary>
    public uint LinkedAccountId { get; private set; }

    public CloudAccountLinkStatus Status { get; private set; }

    public DateTime LinkedAtUtc { get; private set; }

    /// <summary>Null while <see cref="Status"/> is <see cref="CloudAccountLinkStatus.Active"/>.</summary>
    public DateTime? UnlinkedAtUtc { get; private set; }

    /// <summary>
    /// Ends this link (AUTH-005): from this moment on, this account's future deposits no longer
    /// route to the group's Main Account. Does not touch any already-transferred Cloud asset.
    /// </summary>
    internal void Unlink(DateTime nowUtc)
    {
        if (Status != CloudAccountLinkStatus.Active)
        {
            throw new InvalidOperationException($"Account link {Id} is not active and cannot be unlinked again.");
        }

        Status = CloudAccountLinkStatus.Unlinked;
        UnlinkedAtUtc = nowUtc;
    }
}
