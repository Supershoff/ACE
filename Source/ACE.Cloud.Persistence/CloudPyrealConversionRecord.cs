namespace ACE.Cloud.Persistence;

/// <summary>
/// Proves one Raw Pyreal Deposit conversion (DEP-006) already ran to completion, the same role
/// <see cref="CloudIdempotencyRecord"/> plays for every other boundary handoff (ARCH-006,
/// transaction rules 4 and 8). Kept as its own table, distinct from
/// <see cref="CloudIdempotencyRecord"/>, because a conversion creates zero-or-more MMD
/// <see cref="CloudCustodyRecord"/> rows (see <see cref="CloudPyrealConversionMmd"/>) rather than
/// the single custody record every other operation type creates or consumes -- the existing
/// <see cref="CloudIdempotencyRecord"/> shape has no room for that. Written in the same database
/// transaction as the remainder update, the MMD custody records, and the Activity Ledger/outbox
/// rows it describes.
/// </summary>
public sealed class CloudPyrealConversionRecord
{
    private CloudPyrealConversionRecord()
    {
    }

    public CloudPyrealConversionRecord(
        Guid idempotencyKey,
        string shardId,
        Guid ownerId,
        uint rawBiotaId,
        long rawPyrealAmount,
        long remainderBefore,
        long remainderAfter,
        Guid correlationId)
    {
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A Pyreal conversion record requires a non-empty idempotency key.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Pyreal conversion record requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A Pyreal conversion record requires an owner.", nameof(ownerId));
        }

        if (rawBiotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawBiotaId), "A Pyreal conversion record requires the real raw Pyreal biota GUID it consumed.");
        }

        if (rawPyrealAmount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rawPyrealAmount), "A Pyreal conversion record requires a positive raw amount.");
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A Pyreal conversion record requires an Activity Ledger correlation ID.", nameof(correlationId));
        }

        IdempotencyKey = idempotencyKey;
        ShardId = shardId;
        OwnerId = ownerId;
        RawBiotaId = rawBiotaId;
        RawPyrealAmount = rawPyrealAmount;
        RemainderBefore = remainderBefore;
        RemainderAfter = remainderAfter;
        CorrelationId = correlationId;
    }

    public Guid IdempotencyKey { get; private set; }

    public string ShardId { get; private set; } = null!;

    public Guid OwnerId { get; private set; }

    /// <summary>The consumed raw Pyreal coin-stack biota GUID (destroyed by this conversion).</summary>
    public uint RawBiotaId { get; private set; }

    public long RawPyrealAmount { get; private set; }

    public long RemainderBefore { get; private set; }

    public long RemainderAfter { get; private set; }

    public Guid CorrelationId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
