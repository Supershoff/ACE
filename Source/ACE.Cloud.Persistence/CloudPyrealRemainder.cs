using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// An account's exact unconverted Pyreal Remainder (DEP-006, INV-004): "An account's exact
/// unconverted Raw Pyreal balance below the 287,500-Pyreal threshold for creating the next MMD."
/// One row per (<see cref="OwnerId"/>, <see cref="ShardId"/>) pair; a missing row means a remainder
/// of exactly zero. Deliberately not a native biota and not a <see cref="CloudCustodyRecord"/>: it
/// is a plain account-scoped integer, which is exactly why it never counts toward Storage Quota
/// (INV-004's "a Pyreal Remainder does not count toward a Storage Quota") -- Storage Quota
/// projections only ever sum <see cref="CloudCustodyRecord"/>/<see cref="CloudStackLot"/> rows, and
/// this table is neither.
/// </summary>
public sealed class CloudPyrealRemainder
{
    private CloudPyrealRemainder()
    {
    }

    public CloudPyrealRemainder(string shardId, Guid ownerId, long remainderAmount)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Pyreal Remainder requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A Pyreal Remainder requires an owner.", nameof(ownerId));
        }

        if (remainderAmount < 0 || remainderAmount >= PyrealConversionPolicy.PyrealsPerMmd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainderAmount), remainderAmount,
                $"A Pyreal Remainder must be at least 0 and strictly less than {PyrealConversionPolicy.PyrealsPerMmd}.");
        }

        ShardId = shardId;
        OwnerId = ownerId;
        RemainderAmount = remainderAmount;
        Version = 1;
    }

    public string ShardId { get; private set; } = null!;

    public Guid OwnerId { get; private set; }

    public long RemainderAmount { get; private set; }

    /// <summary>Optimistic concurrency token (ARCH-006); unused directly since every mutation holds this row's lock for the whole transaction, kept for parity with every other mutable Cloud aggregate.</summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Replaces this remainder with <paramref name="newRemainderAmount"/> (DEP-006). Callers must
    /// already hold this row's lock for the whole boundary transaction.
    /// </summary>
    internal void Replace(long newRemainderAmount)
    {
        if (newRemainderAmount < 0 || newRemainderAmount >= PyrealConversionPolicy.PyrealsPerMmd)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newRemainderAmount), newRemainderAmount,
                $"A Pyreal Remainder must be at least 0 and strictly less than {PyrealConversionPolicy.PyrealsPerMmd}.");
        }

        RemainderAmount = newRemainderAmount;
        Version++;
    }
}
