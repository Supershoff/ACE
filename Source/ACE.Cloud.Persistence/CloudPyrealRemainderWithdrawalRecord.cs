namespace ACE.Cloud.Persistence;

/// <summary>
/// Proves one raw Pyreal Remainder withdrawal (DEP-006) already ran to completion (ARCH-006,
/// transaction rules 4 and 8). Distinct from <see cref="CloudIdempotencyRecord"/> for the same
/// reason as <see cref="CloudPyrealConversionRecord"/>: a remainder withdrawal delivers
/// zero-or-more native coin biotas rather than the single biota every other withdrawal type
/// delivers.
/// </summary>
public sealed class CloudPyrealRemainderWithdrawalRecord
{
    private CloudPyrealRemainderWithdrawalRecord()
    {
    }

    public CloudPyrealRemainderWithdrawalRecord(
        Guid idempotencyKey,
        string shardId,
        Guid ownerId,
        long amount,
        long remainderBefore,
        long remainderAfter,
        uint recipientContainerId,
        Guid correlationId)
    {
        if (idempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A Pyreal Remainder withdrawal record requires a non-empty idempotency key.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Pyreal Remainder withdrawal record requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A Pyreal Remainder withdrawal record requires an owner.", nameof(ownerId));
        }

        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A Pyreal Remainder withdrawal record requires a positive amount.");
        }

        if (recipientContainerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipientContainerId), "A Pyreal Remainder withdrawal requires a real recipient container GUID.");
        }

        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A Pyreal Remainder withdrawal record requires an Activity Ledger correlation ID.", nameof(correlationId));
        }

        IdempotencyKey = idempotencyKey;
        ShardId = shardId;
        OwnerId = ownerId;
        Amount = amount;
        RemainderBefore = remainderBefore;
        RemainderAfter = remainderAfter;
        RecipientContainerId = recipientContainerId;
        CorrelationId = correlationId;
    }

    public Guid IdempotencyKey { get; private set; }

    public string ShardId { get; private set; } = null!;

    public Guid OwnerId { get; private set; }

    public long Amount { get; private set; }

    public long RemainderBefore { get; private set; }

    public long RemainderAfter { get; private set; }

    public uint RecipientContainerId { get; private set; }

    public Guid CorrelationId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
