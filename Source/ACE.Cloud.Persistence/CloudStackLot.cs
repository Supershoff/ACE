namespace ACE.Cloud.Persistence;

/// <summary>
/// An independently owned or reserved quantity claim against one stackable biota in Cloud custody
/// (ARCH-010, ARCH-011, INV-001, docs/adr/0002-defer-native-materialization-for-partial-stacks.md).
/// Every lot backed by the same <see cref="CloudCustodyRecord"/> sums exactly to that record's
/// TotalQuantity at all times; a database trigger enforces the sum can never be exceeded, and
/// application code (<see cref="CloudStackLotTransactionAuthority"/>,
/// <see cref="CloudCustodyBoundary"/>) preserves exact equality by construction: every mutation
/// that removes quantity from one place adds it back somewhere else in the same transaction.
/// </summary>
public sealed class CloudStackLot
{
    private CloudStackLot()
    {
    }

    public CloudStackLot(Guid custodyRecordId, string shardId, Guid ownerId, int quantity)
    {
        if (custodyRecordId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Stack Lot requires the backing Cloud Custody Record's ID.", nameof(custodyRecordId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Cloud Stack Lot requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Stack Lot requires exactly one owner.", nameof(ownerId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A Cloud Stack Lot requires a positive quantity.");
        }

        Id = Guid.NewGuid();
        CustodyRecordId = custodyRecordId;
        ShardId = shardId;
        OwnerId = ownerId;
        Quantity = quantity;
        Version = 1;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The backing stack <see cref="CloudCustodyRecord"/> this lot claims quantity against.
    /// </summary>
    public Guid CustodyRecordId { get; private set; }

    public string ShardId { get; private set; } = null!;

    /// <summary>
    /// The opaque Cloud ownership identity that exclusively owns this lot's quantity.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// The exclusively claimed quantity, always positive (INV-001's "positive lot quantities").
    /// </summary>
    public int Quantity { get; private set; }

    /// <summary>
    /// Optimistic concurrency token (ARCH-006, transaction rule 3).
    /// </summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Reduces this lot's quantity, leaving a positive remainder, as part of a split or a partial
    /// materializing withdrawal. Callers must add the removed quantity back somewhere else (a new
    /// lot, or a materialized child biota plus a matching CloudCustodyRecord.TotalQuantity
    /// reduction) within the same transaction to preserve conservation.
    /// </summary>
    internal void ReduceQuantity(int amount)
    {
        if (amount <= 0 || amount >= Quantity)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A quantity reduction must be positive and leave a positive remainder.");
        }

        Quantity -= amount;
        Version++;
    }

    /// <summary>
    /// Increases this lot's quantity by exactly the quantity removed from another lot being merged
    /// into it, within the same transaction as that lot's removal (ARCH-011: no auto-merge, only an
    /// explicit merge preserves conservation).
    /// </summary>
    internal void MergeIn(int amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A merge must add a positive quantity.");
        }

        Quantity += amount;
        Version++;
    }

    /// <summary>
    /// Reassigns this lot to a new owner without changing its quantity.
    /// </summary>
    internal void ChangeOwner(Guid newOwnerId)
    {
        if (newOwnerId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Stack Lot requires exactly one owner.", nameof(newOwnerId));
        }

        OwnerId = newOwnerId;
        Version++;
    }
}
