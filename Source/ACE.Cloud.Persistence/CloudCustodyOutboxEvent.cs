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
        Guid ownerId,
        long sequenceNumber)
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

        if (sequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "An outbox event requires a positive sequence number.");
        }

        Id = Guid.NewGuid();
        CorrelationId = correlationId;
        ShardId = shardId;
        EventType = eventType;
        BiotaId = biotaId;
        OwnerId = ownerId;
        SequenceNumber = sequenceNumber;
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudBoundaryOperationType EventType { get; private set; }

    public uint BiotaId { get; private set; }

    public Guid OwnerId { get; private set; }

    /// <summary>
    /// This event's position in the durable total order the companion web service replays the
    /// Custody Outbox in (ARCH-007). Assigned within the same transaction as the rest of this
    /// event's commit by <see cref="CloudCustodyOutboxSequence"/>, so it is strictly increasing in
    /// commit order even under concurrent writers -- unlike <see cref="CreatedAtUtc"/>, which two
    /// events committed in the same database-clock microsecond could otherwise share.
    /// </summary>
    public long SequenceNumber { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
