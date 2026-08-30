namespace ACE.Cloud.Persistence;

/// <summary>
/// One append-only audit entry for a Global Cloud Maintenance entry or exit (ADM-004: "Entry/exit
/// require reason, confirmation, ledger event, and admin webhook"). Written in the same database
/// transaction as the maintenance state change it records (transaction rule 5). Kept as its own
/// dedicated table rather than reusing the biota-shaped <see cref="CloudActivityLedgerEvent"/>, for
/// the same reason <see cref="CloudAccountLinkLedgerEvent"/> is: this event has no native biota or
/// Cloud Custody Record identity of its own.
/// </summary>
public sealed class CloudGlobalMaintenanceLedgerEvent
{
    private CloudGlobalMaintenanceLedgerEvent()
    {
    }

    public CloudGlobalMaintenanceLedgerEvent(
        Guid correlationId,
        string shardId,
        CloudGlobalMaintenanceLedgerEventType eventType,
        string? reason,
        uint? actorAccountId,
        long? frozenDurationSeconds)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A Global Cloud Maintenance ledger event requires a correlation ID.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Global Cloud Maintenance ledger event requires a Cloud Shard ID.", nameof(shardId));
        }

        Id = Guid.NewGuid();
        CorrelationId = correlationId;
        ShardId = shardId;
        EventType = eventType;
        Reason = reason;
        ActorAccountId = actorAccountId;
        FrozenDurationSeconds = frozenDurationSeconds;
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudGlobalMaintenanceLedgerEventType EventType { get; private set; }

    public string? Reason { get; private set; }

    public uint? ActorAccountId { get; private set; }

    /// <summary>Populated only for an <see cref="CloudGlobalMaintenanceLedgerEventType.Exited"/> event: the exact frozen duration, in whole seconds.</summary>
    public long? FrozenDurationSeconds { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }
}
