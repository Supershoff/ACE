namespace ACE.Cloud.Domain;

/// <summary>
/// The kind of actionable private event the Notification Center (EVT-003) surfaces. CONTEXT.md
/// enumerates the eventual full set -- "Transfer Offers, outbids, sales, settlements, sharing
/// changes, reservation outcomes, and administrative actions" -- most of which have no producing
/// code yet (Marketplace, Transfer Offers, and Sharing Grants are later workstreams). Only
/// <see cref="OwnershipReceived"/> is wired to a real event source today
/// (<see cref="CloudNotificationClassifier"/>); the remaining members exist so each later workstream
/// extends this same enum/classifier pair instead of inventing a parallel notification mechanism.
/// </summary>
public enum CloudNotificationKind
{
    /// <summary>An immediate whole-item Cloud ownership transfer landed in this owner's inventory (the persistence layer's "OwnershipTransfer" boundary operation).</summary>
    OwnershipReceived,
}
