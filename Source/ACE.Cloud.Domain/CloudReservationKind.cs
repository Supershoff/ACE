namespace ACE.Cloud.Domain;

/// <summary>
/// The workflow that exclusively owns one kind of Cloud reservation
/// (IMPLEMENTATION-BRIEF.md's core custody state model: "Other reservations end only through their
/// owning workflow"). Only the workflow matching a reservation's <see cref="CloudReservation.Kind"/>
/// may release it (<see cref="CloudReservationPolicy.Release"/>).
/// </summary>
public enum CloudReservationKind
{
    /// <summary>Backs a Withdrawal Token's Withdrawal Reservation (WDR-001).</summary>
    Withdrawal,

    /// <summary>Backs a published marketplace listing's Listing Reservation (MKT-007).</summary>
    Listing,

    /// <summary>Backs a pending Transfer Offer's exclusive hold (XFER-002).</summary>
    Offer,

    /// <summary>Backs a bidder's Bid Escrow allocation.</summary>
    BidEscrow,
}
