using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One Notification Center row (EVT-003), created and coalesced by
/// <see cref="CloudNotificationProjectionConsumer"/> from the Custody Outbox. Unlike the Activity
/// Ledger, this row is intentionally mutable: <see cref="OccurrenceCount"/>/<see cref="LastOccurredAtUtc"/>
/// update in place while unread (<see cref="CloudNotificationCoalescingPolicy"/>), and
/// <see cref="MarkRead"/> is the "visiting an event's destination may mark its notification read
/// automatically" mutation CONTEXT.md describes -- this is presentation state over the immutable
/// ledger/outbox, not a second copy of ledger authority.
/// </summary>
public sealed class CloudNotification
{
    private CloudNotification()
    {
    }

    private CloudNotification(string shardId, Guid ownerId, CloudNotificationKind kind, string destination)
    {
        Id = Guid.NewGuid();
        ShardId = shardId;
        OwnerId = ownerId;
        Kind = kind;
        Destination = destination;
        OccurrenceCount = 0;
        IsRead = false;
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public Guid OwnerId { get; private set; }

    public CloudNotificationKind Kind { get; private set; }

    public string Destination { get; private set; } = null!;

    public int OccurrenceCount { get; private set; }

    public Guid LatestSourceEventId { get; private set; }

    public long LatestSourceSequenceNumber { get; private set; }

    public bool IsRead { get; private set; }

    public DateTime FirstOccurredAtUtc { get; private set; }

    public DateTime LastOccurredAtUtc { get; private set; }

    public DateTime? ReadAtUtc { get; private set; }

    /// <summary>Creates a brand-new notification for its first occurrence, then immediately records that occurrence.</summary>
    public static CloudNotification CreateFirst(
        string shardId, Guid ownerId, CloudNotificationKind kind, string destination, Guid sourceEventId, long sourceSequenceNumber)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A notification requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A notification requires an owner.", nameof(ownerId));
        }

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("A notification requires a contextual destination.", nameof(destination));
        }

        var notification = new CloudNotification(shardId, ownerId, kind, destination);
        notification.RecordOccurrence(sourceEventId, sourceSequenceNumber);
        return notification;
    }

    /// <summary>Coalesces one more occurrence into this still-unread notification (<see cref="CloudNotificationCoalescingPolicy"/>).</summary>
    public void RecordOccurrence(Guid sourceEventId, long sourceSequenceNumber)
    {
        if (sourceEventId == Guid.Empty)
        {
            throw new ArgumentException("A notification occurrence requires the source event's ID.", nameof(sourceEventId));
        }

        if (sourceSequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceSequenceNumber), "A notification occurrence requires a positive source sequence number.");
        }

        OccurrenceCount++;
        LatestSourceEventId = sourceEventId;
        LatestSourceSequenceNumber = sourceSequenceNumber;
    }

    public void MarkRead(DateTime nowUtc)
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadAtUtc = nowUtc;
    }
}
