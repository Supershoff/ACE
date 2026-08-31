namespace ACE.Cloud.Domain;

/// <summary>
/// The exhaustive set of exact reasons <see cref="CloudTransferOfferPolicy"/> can refuse a Transfer
/// Offer creation or terminal command, matching the precedent set by
/// <see cref="CloudAccountLinkRejectionCode"/> (issue #20's acceptance criterion: "every ... blocked
/// condition has an exact response").
/// </summary>
public enum CloudTransferOfferRejectionCode
{
    /// <summary>Not a rejection; the request is approved.</summary>
    None,

    /// <summary>Global Cloud Maintenance or a Marketplace Maintenance Frozen state currently blocks mutation.</summary>
    MutationsFrozen,

    /// <summary>The request named no targets.</summary>
    EmptyRequest,

    /// <summary>A single request named the same target more than once.</summary>
    DuplicateTargetsInRequest,

    /// <summary>No current character matching the recipient's typed name could be resolved.</summary>
    UnknownRecipientCharacter,

    /// <summary>The resolved recipient is the sender's own Main/Linked ownership group.</summary>
    SelfRecipient,

    /// <summary>The resolved recipient belongs to a different Cloud Shard than this offer's own (ARCH-001).</summary>
    CrossShardRecipient,

    /// <summary>Accepting this offer's item count would exceed the recipient's Storage Quota (INV-004..006).</summary>
    RecipientOverQuota,

    /// <summary>At least one requested target already carries an active exclusive reservation.</summary>
    TargetAlreadyReserved,

    /// <summary>A workflow other than Transfer Offer attempted to act on this offer, or a caller supplied the wrong offer for a command.</summary>
    NotFound,

    /// <summary>The offer is not currently Pending, so it has already reached (or cannot reach) a terminal state.</summary>
    NotPending,

    /// <summary>The offer's seven-day deadline has already passed; it must be expired, not accepted or declined.</summary>
    AlreadyExpired,

    /// <summary>The caller's expected aggregate version did not match the offer's current authoritative version.</summary>
    VersionConflict,

    /// <summary>A command was attempted by an account that is neither this offer's sender nor its recipient.</summary>
    NotAuthorized,
}
