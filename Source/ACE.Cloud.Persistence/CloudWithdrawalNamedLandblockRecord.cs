namespace ACE.Cloud.Persistence;

/// <summary>
/// One administrator-named Withdrawal Landblock row (WDR-006), stored as its 16-bit landblock ID
/// (`0x123E` format) rather than a full ACE position -- CONTEXT.md: "a user-named landblock... not a
/// coordinate radius."
/// </summary>
public sealed class CloudWithdrawalNamedLandblockRecord
{
    private CloudWithdrawalNamedLandblockRecord()
    {
    }

    public CloudWithdrawalNamedLandblockRecord(Guid id, string shardId, ushort landblock, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A named Withdrawal Landblock requires a real ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A named Withdrawal Landblock requires a Cloud Shard ID.", nameof(shardId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A named Withdrawal Landblock requires a non-empty name.", nameof(name));
        }

        Id = id;
        ShardId = shardId;
        Landblock = landblock;
        Name = name;
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public ushort Landblock { get; private set; }

    public string Name { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; private set; }
}
