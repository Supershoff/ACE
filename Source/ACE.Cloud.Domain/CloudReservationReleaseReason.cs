namespace ACE.Cloud.Domain;

/// <summary>
/// The exact reason a reservation's owning workflow ended it (WDR-003, MKT-007, XFER-002), recorded
/// on the released <see cref="CloudReservation"/> for Activity Ledger presentation (EVT-002).
/// </summary>
public enum CloudReservationReleaseReason
{
    /// <summary>The reserved target(s) were successfully delivered or transferred to their destination.</summary>
    Fulfilled,

    /// <summary>The owning actor explicitly cancelled the reservation before fulfillment.</summary>
    Cancelled,

    /// <summary>The reservation's time limit elapsed without fulfillment or cancellation.</summary>
    Expired,

    /// <summary>An audited administrator intervention ended the reservation.</summary>
    AdminIntervention,

    /// <summary>
    /// A grant-derived Withdrawal Reservation was released because the Sharing Grant that authorized
    /// it was downgraded/revoked, or the grantee's qualifying allegiance membership that derived it
    /// ended (SHARE-004: "Loss of qualifying guild membership immediately revokes derived access and
    /// invalidates unredeemed Withdrawal Tokens created through it").
    /// </summary>
    SharingGrantAuthorityLost,
}
