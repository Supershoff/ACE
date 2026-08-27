namespace ACE.Cloud.Persistence;

/// <summary>
/// One append-only Activity Ledger entry (EVT-001, EVT-002) for a committed world-boundary
/// handoff. Written in the same database transaction as the custody state change it records
/// (ARCH-006, transaction rule 5); there is no update or delete path.
/// </summary>
public sealed class CloudActivityLedgerEvent
{
    private CloudActivityLedgerEvent()
    {
    }

    public CloudActivityLedgerEvent(
        Guid correlationId,
        string shardId,
        CloudBoundaryOperationType eventType,
        uint biotaId,
        Guid ownerId,
        CloudBoundaryOutcomeKind outcome,
        string? reason = null)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A ledger event requires a correlation ID.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A ledger event requires a Cloud Shard ID.", nameof(shardId));
        }

        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "A ledger event requires a real native biota GUID.");
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A ledger event requires an owner.", nameof(ownerId));
        }

        Id = Guid.NewGuid();
        CorrelationId = correlationId;
        ShardId = shardId;
        EventType = eventType;
        BiotaId = biotaId;
        OwnerId = ownerId;
        Outcome = outcome;
        Reason = reason;
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudBoundaryOperationType EventType { get; private set; }

    public uint BiotaId { get; private set; }

    public Guid OwnerId { get; private set; }

    public CloudBoundaryOutcomeKind Outcome { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>
    /// Database time (transaction rule 1), not application/browser time.
    /// </summary>
    public DateTime OccurredAtUtc { get; private set; }
}
