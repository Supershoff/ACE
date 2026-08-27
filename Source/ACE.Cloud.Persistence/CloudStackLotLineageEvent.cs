namespace ACE.Cloud.Persistence;

/// <summary>
/// One append-only record of ACE materializing a native child biota for part of a Cloud Stack Lot
/// (INV-003: "ACE materialization produces new child GUIDs through native allocation and logs
/// complete lineage"). Written in the same database transaction as the materialization it records
/// (ARCH-006, transaction rule 5); there is no update or delete path.
/// </summary>
public sealed class CloudStackLotLineageEvent
{
    private CloudStackLotLineageEvent()
    {
    }

    public CloudStackLotLineageEvent(
        Guid correlationId, string shardId, uint parentBiotaId, uint childBiotaId, int quantity, Guid ownerId)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A lineage event requires a correlation ID.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A lineage event requires a Cloud Shard ID.", nameof(shardId));
        }

        if (parentBiotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parentBiotaId), "A lineage event requires a real parent native biota GUID.");
        }

        if (childBiotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(childBiotaId), "A lineage event requires a real child native biota GUID.");
        }

        if (childBiotaId == parentBiotaId)
        {
            throw new ArgumentException("A materialized child must have a different GUID from its parent.", nameof(childBiotaId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A lineage event requires a positive materialized quantity.");
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A lineage event requires an owner.", nameof(ownerId));
        }

        Id = Guid.NewGuid();
        CorrelationId = correlationId;
        ShardId = shardId;
        ParentBiotaId = parentBiotaId;
        ChildBiotaId = childBiotaId;
        Quantity = quantity;
        OwnerId = ownerId;
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    /// <summary>
    /// The original, still-custodied backing biota this child was materialized from.
    /// </summary>
    public uint ParentBiotaId { get; private set; }

    /// <summary>
    /// The new native biota ACE materialized under its own GUID allocation.
    /// </summary>
    public uint ChildBiotaId { get; private set; }

    /// <summary>
    /// The quantity carried by the materialized child.
    /// </summary>
    public int Quantity { get; private set; }

    public Guid OwnerId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
