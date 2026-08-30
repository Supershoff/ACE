namespace ACE.Cloud.Domain;

/// <summary>
/// One read-only Notification Center row (EVT-003) as returned to a caller: an immutable snapshot,
/// never a handle back to the mutable persistence entity. <see cref="OccurrenceCount"/> is
/// CONTEXT.md's coalescing counter -- how many notification-worthy events this row currently
/// represents.
/// </summary>
public sealed record CloudNotificationSnapshot(
    Guid Id,
    CloudNotificationKind Kind,
    string Destination,
    int OccurrenceCount,
    bool IsRead,
    DateTime FirstOccurredAtUtc,
    DateTime LastOccurredAtUtc);

/// <summary>The Notification Center's unread badge (CONTEXT.md: "presented through an unread badge and contextual destinations").</summary>
public sealed record CloudNotificationUnreadSummary(int UnreadCount);
