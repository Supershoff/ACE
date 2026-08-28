namespace ACE.Cloud.Domain;

/// <summary>
/// One submitted Cloud Custodian sell-window row, already resolved by ACE's own world-boundary code
/// to the exact facts <see cref="CloudCustodianDepositRowPolicy.Decide"/> needs (DEP-002: "validate
/// and commit each independently"). Building this from a live ACE <c>WorldObject</c>/<c>ItemProfile</c>
/// pair is ACE-side responsibility (ARCH-002); this project stays pure (ARCH-012).
/// </summary>
public sealed record CloudCustodianDepositRowRequest
{
    public CloudItemId ItemId { get; init; }

    /// <summary>The quantity the player's client submitted for this row.</summary>
    public int SubmittedAmount { get; init; }

    /// <summary>The item's current persisted stack size; 1 for a non-stackable item.</summary>
    public int CurrentStackSize { get; init; }

    /// <summary>
    /// Whether this item's weenie type stacks at all (DEP-005/ADR-0002: a stackable row deposits as
    /// a stack Cloud Custody Record plus an initial Cloud Stack Lot; a non-stackable row deposits as
    /// a whole-item Cloud Custody Record).
    /// </summary>
    public bool IsStackable { get; init; }

    /// <summary>Another row earlier in the same submission already claimed this exact item GUID.</summary>
    public bool IsDuplicateInSubmission { get; init; }

    public CloudItemEligibilitySnapshot Snapshot { get; init; }

    /// <summary>
    /// Set only for a raw Pyreal coin-stack row (DEP-006): the exact total Pyreal value ACE
    /// observed on the live coin stack at deposit time (its <c>Value</c>, which ACE keeps equal to
    /// <c>StackUnitValue * StackSize</c> for a coin stack -- not its coin count). Null for every
    /// other row, which deposits as an ordinary Cloud Item instead of converting.
    /// </summary>
    public long? RawPyrealAmount { get; init; }

    public CloudCustodianDepositRowRequest(
        CloudItemId itemId,
        int submittedAmount,
        int currentStackSize,
        bool isStackable,
        bool isDuplicateInSubmission,
        CloudItemEligibilitySnapshot snapshot,
        long? rawPyrealAmount = null)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(snapshot);

        ItemId = itemId;
        SubmittedAmount = submittedAmount;
        CurrentStackSize = currentStackSize;
        IsStackable = isStackable;
        IsDuplicateInSubmission = isDuplicateInSubmission;
        Snapshot = snapshot;
        RawPyrealAmount = rawPyrealAmount;
    }
}
