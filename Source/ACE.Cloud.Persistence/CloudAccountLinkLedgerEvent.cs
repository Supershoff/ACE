namespace ACE.Cloud.Persistence;

/// <summary>
/// One append-only audit entry for an account link/unlink attempt, approved or rejected
/// (EVT-001, EVT-002: "one append-only immutable Activity Ledger covers... ownership... linking").
/// Written in the same database transaction as the link/unlink state change it records
/// (transaction rule 5). Kept as its own dedicated table rather than reusing the biota-shaped
/// <see cref="CloudActivityLedgerEvent"/> for the same reason <see cref="CloudIdentityOutboxEvent"/>
/// is separate from <see cref="CloudCustodyOutboxEvent"/>: linking has no native biota or Cloud
/// Custody Record identity of its own. A later Cloud Transaction Authority ledger unification
/// (IMPLEMENTATION-BRIEF.md's "one transaction application layer", issue #21) is expected to fold
/// this into one general-purpose ledger; this table exists now so AUTH-005..009 activity is audited
/// from the moment linking exists rather than waiting on that later issue.
/// </summary>
public sealed class CloudAccountLinkLedgerEvent
{
    private CloudAccountLinkLedgerEvent()
    {
    }

    public CloudAccountLinkLedgerEvent(
        Guid correlationId,
        string shardId,
        CloudAccountLinkLedgerEventType eventType,
        uint mainAccountId,
        uint sourceAccountId,
        string? reason)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("An account link ledger event requires a correlation ID.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An account link ledger event requires a Cloud Shard ID.", nameof(shardId));
        }

        if (mainAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mainAccountId), "An account link ledger event requires a real Main Account ID.");
        }

        if (sourceAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAccountId), "An account link ledger event requires a real source account ID.");
        }

        Id = Guid.NewGuid();
        CorrelationId = correlationId;
        ShardId = shardId;
        EventType = eventType;
        MainAccountId = mainAccountId;
        SourceAccountId = sourceAccountId;
        Reason = reason;
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudAccountLinkLedgerEventType EventType { get; private set; }

    public uint MainAccountId { get; private set; }

    public uint SourceAccountId { get; private set; }

    public string? Reason { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }
}
