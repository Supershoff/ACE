namespace ACE.Cloud.Domain;

/// <summary>
/// Every fact <see cref="CloudAllegianceVaultActionPolicy.AuthorizeActingCharacter"/> needs to decide
/// one Allegiance Vault contribute/take attempt (issue #37: VAULT-001, VAULT-002, INV-004..006). The
/// caller (the Cloud Transaction Authority's own gateway) gathers each fact directly against ACE's
/// live persisted state -- never the versioned identity/allegiance cache -- immediately before this
/// evaluation, matching <see cref="CloudTransferOfferCreateRequest"/>'s established shape: this type
/// carries no database access of its own, keeping the authorization decision pure and independently
/// testable.
/// </summary>
public sealed record CloudAllegianceVaultActionRequest
{
    /// <summary>True when the Acting Character ID resolved to a real, current (non-deleted) ACE character.</summary>
    public bool ActingCharacterFound { get; }

    /// <summary>
    /// The Acting Character's own live current monarch (VAULT-001): themselves, if they are
    /// themselves a live monarch with no superior; otherwise their persisted Monarch instance
    /// property; null when they currently belong to no allegiance at all. Only meaningful when
    /// <see cref="ActingCharacterFound"/> is true.
    /// </summary>
    public uint? ActingCharacterCurrentMonarchId { get; }

    /// <summary>
    /// The vault action's destination's current projected Storage Quota count (native biotas plus
    /// projected materialized lots) -- the Allegiance Vault itself for a contribution, or the Acting
    /// Character's own effective Main Account for a take -- excluding this action's own item.
    /// </summary>
    public int DestinationCurrentProjectedCount { get; }

    /// <summary>The destination's Storage Quota limit; null when unlimited.</summary>
    public int? DestinationQuotaLimit { get; }

    public CloudMutationGateState MutationGateState { get; }

    public CloudAllegianceVaultActionRequest(
        bool actingCharacterFound,
        uint? actingCharacterCurrentMonarchId,
        int destinationCurrentProjectedCount,
        int? destinationQuotaLimit,
        CloudMutationGateState mutationGateState)
    {
        if (destinationCurrentProjectedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationCurrentProjectedCount), "A projected item count cannot be negative.");
        }

        ActingCharacterFound = actingCharacterFound;
        ActingCharacterCurrentMonarchId = actingCharacterFound ? actingCharacterCurrentMonarchId : null;
        DestinationCurrentProjectedCount = destinationCurrentProjectedCount;
        DestinationQuotaLimit = destinationQuotaLimit;
        MutationGateState = mutationGateState;
    }
}
