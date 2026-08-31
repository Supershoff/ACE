namespace ACE.Cloud.Domain;

/// <summary>
/// Every fact <see cref="CloudTransferOfferPolicy.Create"/> needs to decide one Transfer Offer
/// creation attempt (XFER-001, XFER-002, INV-002, INV-004..006). The caller (the Cloud Transaction
/// Authority's own gateway) gathers each fact under its own locked commit-time revalidation --
/// resolving the recipient's current character name to an immutable Main Account ID exactly once,
/// locking every requested target in deterministic order, and reading the recipient's current
/// projected Storage Quota usage -- so this type carries no database access of its own, keeping the
/// creation decision itself pure and independently testable, matching
/// <see cref="CloudAccountLinkRequest"/>'s established shape.
/// </summary>
public sealed record CloudTransferOfferCreateRequest
{
    public CloudAccountId SenderAccountId { get; }

    /// <summary>True when the sender's typed recipient character name resolved to a real current character.</summary>
    public bool RecipientCharacterFound { get; }

    /// <summary>The resolved recipient's effective Main Account ID; null when <see cref="RecipientCharacterFound"/> is false.</summary>
    public CloudAccountId? RecipientAccountId { get; }

    /// <summary>True when the resolved recipient belongs to a different Cloud Shard than this offer's own (ARCH-001).</summary>
    public bool RecipientIsCrossShard { get; }

    public IReadOnlyList<CloudReservationTarget> Targets { get; }

    /// <summary>Every currently active allocation (any reservation kind) already claiming one of <see cref="Targets"/>.</summary>
    public IReadOnlyDictionary<CloudReservationTarget, CloudReservationAllocation> ExistingAllocationsByTarget { get; }

    /// <summary>The recipient's current projected Storage Quota count (native biotas plus projected materialized lots), excluding this offer.</summary>
    public int RecipientCurrentProjectedCount { get; }

    /// <summary>The recipient's Storage Quota limit; null when unlimited.</summary>
    public int? RecipientQuotaLimit { get; }

    public CloudMutationGateState MutationGateState { get; }

    public CloudTransferOfferCreateRequest(
        CloudAccountId senderAccountId,
        bool recipientCharacterFound,
        CloudAccountId? recipientAccountId,
        bool recipientIsCrossShard,
        IReadOnlyList<CloudReservationTarget> targets,
        IReadOnlyDictionary<CloudReservationTarget, CloudReservationAllocation> existingAllocationsByTarget,
        int recipientCurrentProjectedCount,
        int? recipientQuotaLimit,
        CloudMutationGateState mutationGateState)
    {
        ArgumentNullException.ThrowIfNull(senderAccountId);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(existingAllocationsByTarget);

        if (recipientCurrentProjectedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recipientCurrentProjectedCount), "A projected item count cannot be negative.");
        }

        SenderAccountId = senderAccountId;
        RecipientCharacterFound = recipientCharacterFound;
        RecipientAccountId = recipientAccountId;
        RecipientIsCrossShard = recipientIsCrossShard;
        Targets = targets;
        ExistingAllocationsByTarget = existingAllocationsByTarget;
        RecipientCurrentProjectedCount = recipientCurrentProjectedCount;
        RecipientQuotaLimit = recipientQuotaLimit;
        MutationGateState = mutationGateState;
    }
}
