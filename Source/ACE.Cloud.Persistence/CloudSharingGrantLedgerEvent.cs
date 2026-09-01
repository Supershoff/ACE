namespace ACE.Cloud.Persistence;

/// <summary>
/// One append-only audit entry for a Sharing Grant lifecycle event (EVT-001, EVT-002). Written in the
/// same database transaction as the grant change it records (transaction rule 5). Kept as its own
/// dedicated table, admin-scoped for now, exactly like <see cref="CloudAccountLinkLedgerEvent"/>'s own
/// documented rationale: a Sharing Grant has two distinct parties (owner and grantee) rather than one
/// biota-scoped owner, so it does not fit the biota-shaped <see cref="CloudActivityLedgerEvent"/>
/// table either. Per-party (owner/grantee) ledger visibility -- CONTEXT.md's eventual "users see
/// ledger activity involving their assets or actions" -- is deferred to the same later Cloud
/// Transaction Authority ledger unification <see cref="CloudAccountLinkLedgerEvent"/> already defers
/// to; owner/grantee-facing awareness in the meantime comes from the Notification Center
/// (<see cref="ACE.Cloud.Domain.CloudNotificationKind.SharingGrantChanged"/>, EVT-003).
/// </summary>
public sealed class CloudSharingGrantLedgerEvent
{
    private CloudSharingGrantLedgerEvent()
    {
    }

    public CloudSharingGrantLedgerEvent(
        Guid correlationId,
        string shardId,
        CloudSharingGrantLedgerEventType eventType,
        Guid ownerId,
        Guid granteeId,
        string? reason)
    {
        if (correlationId == Guid.Empty)
        {
            throw new ArgumentException("A Sharing Grant ledger event requires a correlation ID.", nameof(correlationId));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Sharing Grant ledger event requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A Sharing Grant ledger event requires an owner.", nameof(ownerId));
        }

        if (granteeId == Guid.Empty)
        {
            throw new ArgumentException("A Sharing Grant ledger event requires a grantee.", nameof(granteeId));
        }

        Id = Guid.NewGuid();
        CorrelationId = correlationId;
        ShardId = shardId;
        EventType = eventType;
        OwnerId = ownerId;
        GranteeId = granteeId;
        Reason = reason;
    }

    public Guid Id { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public CloudSharingGrantLedgerEventType EventType { get; private set; }

    public Guid OwnerId { get; private set; }

    public Guid GranteeId { get; private set; }

    public string? Reason { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }
}
