namespace ACE.Cloud.Domain;

/// <summary>
/// CONTEXT.md's "repetitive events are coalesced": whether a freshly arrived notification-worthy
/// event should merge into an existing notification row rather than create a new one. Pure and
/// storage-agnostic so it is unit-testable without a database; the persistence-layer consumer
/// (<c>ACE.Cloud.Persistence.CloudNotificationProjectionConsumer</c>) is the only caller and is
/// responsible for actually looking up "the most recent notification of this kind for this owner."
/// </summary>
public static class CloudNotificationCoalescingPolicy
{
    /// <summary>
    /// A new event of <paramref name="incomingKind"/> coalesces into an existing notification only
    /// when that notification is still unread and of the exact same kind. A read notification never
    /// accepts a coalesced update -- CONTEXT.md's "visiting an event's destination may mark its
    /// notification read automatically" would otherwise silently resurrect an already-acknowledged
    /// notification as unread again, which is indistinguishable from a fresh one to the user.
    /// </summary>
    public static bool ShouldCoalesce(CloudNotificationKind existingKind, bool existingIsRead, CloudNotificationKind incomingKind) =>
        !existingIsRead && existingKind == incomingKind;
}
