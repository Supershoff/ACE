namespace ACE.Cloud.Persistence;

/// <summary>
/// The single counter row that assigns each <see cref="CloudCustodyOutboxEvent"/> its durable,
/// strictly increasing <see cref="CloudCustodyOutboxEvent.SequenceNumber"/> (ARCH-007). Reserving
/// the next value locks this row (<c>SELECT ... FOR UPDATE</c>) within the same transaction that
/// commits the outbox event, so concurrent boundary transactions serialize on this one row instead
/// of racing to assign the same sequence number -- the same deterministic-locking approach this
/// schema already uses for Cloud Stack Lot conservation. One row exists per deployment (ARCH-001:
/// exactly one Cloud Shard per deployment), seeded by the migration that introduces this table.
/// </summary>
public sealed class CloudCustodyOutboxSequence
{
    private CloudCustodyOutboxSequence()
    {
    }

    public int Id { get; private set; } = 1;

    public long NextValue { get; private set; }
}
