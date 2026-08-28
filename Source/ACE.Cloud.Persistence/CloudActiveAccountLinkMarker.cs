namespace ACE.Cloud.Persistence;

/// <summary>
/// The actual enforcement (via its primary key) that one ACE account can be an active Linked
/// Account of at most one <see cref="CloudOwnershipGroup"/> at a time (AUTH-006). Exists as its own
/// tiny table -- rather than a partial/filtered unique index on <see cref="CloudAccountLink"/>,
/// which MariaDB cannot express -- because <see cref="CloudAccountLink"/> retains every historical
/// row (including past Unlinked ones) for audit, so a plain unique index on
/// (ShardId, LinkedAccountId) would incorrectly reject a legitimate second link cycle for the same
/// account. A row here exists exactly while the corresponding <see cref="CloudAccountLink"/> is
/// <see cref="CloudAccountLinkStatus.Active"/>; <c>CloudAccountLinkGateway</c> deletes it in the same
/// transaction that unlinks. Also answers "does this account currently have active Linked Accounts
/// of its own" (AUTH-006's "not a Main with children") by joining <see cref="OwnershipGroupId"/>
/// back to <see cref="CloudOwnershipGroup.MainAccountId"/>.
/// </summary>
public sealed class CloudActiveAccountLinkMarker
{
    private CloudActiveAccountLinkMarker()
    {
    }

    public CloudActiveAccountLinkMarker(string shardId, uint accountId, Guid accountLinkId, Guid ownershipGroupId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An active account link marker requires a Cloud Shard ID.", nameof(shardId));
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "An active account link marker requires a real account ID.");
        }

        if (accountLinkId == Guid.Empty)
        {
            throw new ArgumentException("An active account link marker requires its account link ID.", nameof(accountLinkId));
        }

        if (ownershipGroupId == Guid.Empty)
        {
            throw new ArgumentException("An active account link marker requires its ownership group ID.", nameof(ownershipGroupId));
        }

        ShardId = shardId;
        AccountId = accountId;
        AccountLinkId = accountLinkId;
        OwnershipGroupId = ownershipGroupId;
    }

    public string ShardId { get; private set; } = null!;

    /// <summary>The account this marker proves is currently an active Linked Account (AUTH-006).</summary>
    public uint AccountId { get; private set; }

    public Guid AccountLinkId { get; private set; }

    public Guid OwnershipGroupId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
