namespace ACE.Cloud.Persistence;

/// <summary>
/// One durable, versioned Live State Stream entry (EVT-007): "public Marketplace changes and
/// authorized private inventory, reservation, bid, listing, offer, and notification changes
/// propagate through a Live State Stream." Written by a projection consumer in the same transaction
/// as the projection row it just updated, immediately after that row's
/// <c>CloudProjectionSequenceGuard</c> check reports the source event was actually newly applied (a
/// duplicate/stale outbox delivery must never publish a second live-stream entry for the same
/// change). Durable and queryable by <see cref="CloudLiveStreamReader"/> rather than an in-memory
/// broadcast-only channel, so a reconnecting client can resume from its own last-seen
/// <see cref="SequenceNumber"/> instead of losing whatever happened while it was disconnected
/// ("cross-tab reconnection"/"missed-event replay").
/// </summary>
public sealed class CloudLiveStreamEvent
{
    private CloudLiveStreamEvent()
    {
    }

    public CloudLiveStreamEvent(
        string shardId,
        long sequenceNumber,
        bool isPublic,
        Guid? scopeOwnerId,
        string eventKind,
        Guid sourceEventId,
        long sourceSequenceNumber)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Live State Stream event requires a Cloud Shard ID.", nameof(shardId));
        }

        if (sequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), "A Live State Stream event requires a positive sequence number.");
        }

        if (!isPublic && (scopeOwnerId is null || scopeOwnerId.Value == Guid.Empty))
        {
            throw new ArgumentException("A private Live State Stream event requires a non-empty scope owner.", nameof(scopeOwnerId));
        }

        if (string.IsNullOrWhiteSpace(eventKind))
        {
            throw new ArgumentException("A Live State Stream event requires an event kind.", nameof(eventKind));
        }

        if (sourceEventId == Guid.Empty)
        {
            throw new ArgumentException("A Live State Stream event requires its source event's ID.", nameof(sourceEventId));
        }

        if (sourceSequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSequenceNumber), "A Live State Stream event requires a positive source sequence number.");
        }

        Id = Guid.NewGuid();
        ShardId = shardId;
        SequenceNumber = sequenceNumber;
        IsPublic = isPublic;
        ScopeOwnerId = isPublic ? null : scopeOwnerId;
        EventKind = eventKind;
        SourceEventId = sourceEventId;
        SourceSequenceNumber = sourceSequenceNumber;
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    /// <summary>This stream's own durable, strictly increasing total order (mirrors <see cref="CloudCustodyOutboxEvent.SequenceNumber"/>'s guarantee).</summary>
    public long SequenceNumber { get; private set; }

    public bool IsPublic { get; private set; }

    /// <summary>Null for a public event; the authorized owner for a private event (<see cref="ACE.Cloud.Domain.CloudLiveStreamAuthorizationPolicy"/>).</summary>
    public Guid? ScopeOwnerId { get; private set; }

    /// <summary>For example "Deposit", "Withdrawal", or "OwnershipTransfer".</summary>
    public string EventKind { get; private set; } = null!;

    /// <summary>The originating Custody/Identity Outbox event's own ID, for audit/correlation.</summary>
    public Guid SourceEventId { get; private set; }

    /// <summary>The originating outbox event's own sequence number, for audit/correlation.</summary>
    public long SourceSequenceNumber { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
}
