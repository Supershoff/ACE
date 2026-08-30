namespace ACE.Cloud.Domain;

/// <summary>
/// Decides whether a Custody Outbox event kind is notification-worthy (EVT-003), and if so, which
/// <see cref="CloudNotificationKind"/> and contextual web destination it maps to. Takes the event
/// kind as the same plain string <c>ACE.Cloud.Persistence.CloudLiveStreamEvent.EventKind</c> already
/// stores (<c>CloudBoundaryOperationType.ToString()</c>) rather than the enum itself, so this pure
/// domain project stays free of a dependency on the persistence layer that owns that enum -- exactly
/// the same string-based seam the Live State Stream already uses.
///
/// Only "OwnershipTransfer" is classified today. CONTEXT.md's full Notification Center scope (offers,
/// outbids, sales, settlements, sharing changes, other reservation outcomes, admin actions) has no
/// producing event source yet -- Marketplace, Transfer Offers, and Sharing Grants are later
/// workstreams -- so each of those lands its own case here when its producing workstream ships,
/// rather than this classifier guessing at a shape for an event that cannot occur yet.
/// </summary>
public static class CloudNotificationClassifier
{
    public static bool TryClassify(string eventType, out CloudNotificationKind kind, out string destination)
    {
        if (eventType == "OwnershipTransfer")
        {
            kind = CloudNotificationKind.OwnershipReceived;
            destination = "/dashboard";
            return true;
        }

        kind = default;
        destination = "";
        return false;
    }
}
