namespace ACE.Cloud.Persistence;

/// <summary>
/// One durable Custody Outbox entry (ARCH-007): notification intent for a committed world-boundary
/// handoff, written in the same database transaction as the custody state change and Activity
/// Ledger entry it accompanies (transaction rule 5). The companion web service consumes these
/// idempotently to rebuild its read models; delivery is out of this issue's scope.
/// </summary>
public sealed class CloudCustodyOutboxEvent
{
    private CloudCustodyOutboxEvent()
    {
    }

    public CloudCustodyOutboxEvent(
        Guid correlationId,
        string shardId,
        CloudBoundaryOperationType eventType,
        uint biotaId,
        Guid ownerId)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("An outbox event requires a correlation ID.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An outbox event requires a Cloud Shard ID.", nameof(shardId));
        }

        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "An outbox event requires a real native biota GUID.");
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("An outbox event requires an owner.", nameof(ownerId));
        }

        Id = Guid.NewGuid();
        CorrelationId = correlationId;
        ShardId = shardId;
        EventType = eventType;
        BiotaId = biotaId;
        OwnerId = ownerId;
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudBoundaryOperationType EventType { get; private set; }

    public uint BiotaId { get; private set; }

    public Guid OwnerId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
