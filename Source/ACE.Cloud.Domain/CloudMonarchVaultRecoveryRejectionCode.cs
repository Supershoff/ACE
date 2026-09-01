namespace ACE.Cloud.Domain;

/// <summary>
/// The exhaustive set of exact reasons an audited administrator Allegiance Vault recovery
/// (VAULT-005, ADM-002) can be refused, matching <see cref="CloudCustodyTransitionErrorKind"/>'s own
/// established "exact domain error suitable for in-game and web presentation" precedent.
/// </summary>
public enum CloudMonarchVaultRecoveryRejectionCode
{
    /// <summary>The caller's ACE administrator access level (ADM-001) could not be revalidated fresh for this exact request.</summary>
    Unauthorized,

    /// <summary>Global Cloud Maintenance or a Marketplace Maintenance Frozen state currently blocks mutation.</summary>
    MutationsFrozen,

    /// <summary>No unresolved out-of-band monarch deletion diagnostic matches the request.</summary>
    DiagnosticNotFound,

    /// <summary>
    /// This diagnostic already has a recorded administrator decision. A committed recovery can never
    /// be overridden by a later attempt (ADM-002: "cannot override a closed committed settlement").
    /// </summary>
    AlreadyResolved,

    /// <summary>An administrator recovery requires a non-blank written reason (ADM-002).</summary>
    ReasonRequired,

    /// <summary>An administrator recovery requires an explicit delayed confirmation (ADM-002, AUTH-007's own established pattern).</summary>
    NotConfirmed,

    /// <summary>The administrator-chosen destination is missing or identical to the orphaned vault itself.</summary>
    InvalidDestination,
}
