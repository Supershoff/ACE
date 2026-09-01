namespace ACE.Cloud.Domain;

/// <summary>
/// The exhaustive set of exact reasons <see cref="CloudAllegianceVaultActionPolicy"/> can refuse a
/// vault contribute/take attempt's Acting Character authorization (issue #37: VAULT-001, VAULT-002,
/// VAULT-003, INV-004..006), matching the precedent set by <see cref="CloudTransferOfferRejectionCode"/>.
/// Item-level facts (current ownership, active reservation) are gathered and checked directly by the
/// Cloud Transaction Authority's own gateway under its row lock, matching
/// <see cref="CloudTransferOfferGateway"/>'s own established split between this pure precondition and
/// the gateway's own locked ownership checks -- they are reported as ordinary conflicts, not a code
/// here.
/// </summary>
public enum CloudAllegianceVaultActionRejectionCode
{
    /// <summary>Not a rejection; the request is approved.</summary>
    None,

    /// <summary>Global Cloud Maintenance or a Marketplace Maintenance Frozen state currently blocks mutation.</summary>
    MutationsFrozen,

    /// <summary>No current, non-deleted character matching the Acting Character ID could be resolved.</summary>
    ActingCharacterNotFound,

    /// <summary>
    /// The Acting Character does not currently belong to any allegiance (VAULT-001: "membership on
    /// one character does not grant unrelated alts access" -- a character with no live current
    /// monarch has no Allegiance Vault to act for, regardless of what its account's other characters
    /// belong to).
    /// </summary>
    ActingCharacterNotInAllegiance,

    /// <summary>Completing this action's item count would exceed the destination's Storage Quota (INV-004..006).</summary>
    DestinationOverQuota,
}
