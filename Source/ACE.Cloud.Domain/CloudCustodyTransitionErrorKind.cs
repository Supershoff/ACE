namespace ACE.Cloud.Domain;

/// <summary>
/// The exhaustive set of exact domain errors an illegal ownership or reservation transition can
/// produce (Green section: "invalid transitions return exact domain errors suitable for in-game and
/// web presentation"), shared by <see cref="CloudReservationPolicy"/> and
/// <see cref="CloudOwnershipTransferPolicy"/> so both surfaces present the same vocabulary.
/// </summary>
public enum CloudCustodyTransitionErrorKind
{
    /// <summary>The request itself is malformed independent of any current state (for example no targets).</summary>
    InvalidRequest,

    /// <summary>A single request named the same target more than once.</summary>
    DuplicateTargetsInRequest,

    /// <summary>At least one requested target already carries an active exclusive reservation.</summary>
    TargetAlreadyReserved,

    /// <summary>The caller's expected aggregate version did not match the current authoritative version.</summary>
    VersionConflict,

    /// <summary>The reservation was already released and cannot be released again.</summary>
    AlreadyReleased,

    /// <summary>A workflow other than the reservation's own kind attempted to release it.</summary>
    WrongReleasingWorkflow,

    /// <summary>An expired reservation was presented for fulfillment instead of an Expired release.</summary>
    CannotFulfillExpiredReservation,

    /// <summary>Global Cloud Maintenance or a Marketplace Maintenance Frozen state currently blocks mutation.</summary>
    MutationsFrozen,
}
