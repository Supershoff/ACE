namespace ACE.Cloud.Domain;

/// <summary>
/// A Transfer Offer's exact position in IMPLEMENTATION-BRIEF.md's Transfer Offer state machine
/// (XFER-002): <c>PENDING_RESERVED</c>, then exactly one terminal state.
/// </summary>
public enum CloudTransferOfferStatus
{
    /// <summary>Reserved and awaiting the recipient's decision, sender cancellation, or expiry.</summary>
    Pending,

    /// <summary>The recipient accepted; every offered target transferred to them atomically.</summary>
    Accepted,

    /// <summary>The recipient declined; every offered target's reservation released back to the sender.</summary>
    Declined,

    /// <summary>The sender cancelled before acceptance; every offered target's reservation released back to the sender.</summary>
    Cancelled,

    /// <summary>Seven days passed with no decision; every offered target's reservation released back to the sender.</summary>
    Expired,
}
