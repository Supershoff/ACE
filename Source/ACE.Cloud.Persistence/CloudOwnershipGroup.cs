namespace ACE.Cloud.Persistence;

/// <summary>
/// The shard-scoped ownership group rooted at one Main Account (AUTH-001..009, CONTEXT.md's "Main
/// Account owns all Cloud assets transferred from each of its Linked Accounts"). Created the first
/// time a Main Account links a source account; never created for an account that has not yet linked
/// anyone. <see cref="CloudAccountLink"/> rows are this group's Linked Accounts, current and former.
/// </summary>
public sealed class CloudOwnershipGroup
{
    private CloudOwnershipGroup()
    {
    }

    public CloudOwnershipGroup(string shardId, uint mainAccountId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An ownership group requires a Cloud Shard ID.", nameof(shardId));
        }

        if (mainAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mainAccountId), "An ownership group requires a real Main Account ID.");
        }

        Id = Guid.NewGuid();
        ShardId = shardId;
        MainAccountId = mainAccountId;
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    /// <summary>The immutable ACE account ID that owns this group's unified Cloud Inventory (AUTH-004).</summary>
    public uint MainAccountId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
