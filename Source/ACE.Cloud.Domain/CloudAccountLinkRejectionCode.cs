namespace ACE.Cloud.Domain;

/// <summary>
/// The exhaustive set of exact reasons <see cref="CloudAccountLinkPolicy"/> can refuse a link or
/// unlink request (issue #20's acceptance criterion: "Every destructive confirmation and blocked
/// condition has an exact response").
/// </summary>
public enum CloudAccountLinkRejectionCode
{
    /// <summary>Not a rejection; the request is approved.</summary>
    None,

    /// <summary>Global Cloud Maintenance or a Marketplace Maintenance Frozen state currently blocks mutation.</summary>
    MutationsFrozen,

    /// <summary>The source and Main Account are the same account.</summary>
    SameAccount,

    /// <summary>
    /// The proposed Main Account is itself a Linked Account of a different group (AUTH-006: "Link
    /// trees/group merges are prohibited").
    /// </summary>
    MainAccountIsLinkedElsewhere,

    /// <summary>The source account is already a Linked Account of some Main Account (AUTH-006).</summary>
    SourceAlreadyLinked,

    /// <summary>The source account is itself a Main Account with its own Linked Accounts (AUTH-006).</summary>
    SourceHasLinkedAccounts,

    /// <summary>
    /// The source account holds an active reservation, listing, bid, settlement, Withdrawal Token,
    /// Transfer Offer, or other in-flight obligation (AUTH-006).
    /// </summary>
    SourceHasPendingObligations,

    /// <summary>Linking would create a seller/bidder self-dealing conflict in an active auction (AUTH-009).</summary>
    WouldCreateAuctionConflict,

    /// <summary>The named link is not currently active, so there is nothing to unlink.</summary>
    LinkNotActive,
}
