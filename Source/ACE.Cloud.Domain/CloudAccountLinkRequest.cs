namespace ACE.Cloud.Domain;

/// <summary>
/// Every fact <see cref="CloudAccountLinkPolicy.EvaluateLink"/> needs to decide one link attempt
/// (AUTH-005, AUTH-006, AUTH-009). The caller (<c>CloudAccountLinkGateway</c>) gathers each flag
/// under its own locked commit-time revalidation; this type carries no database access of its own,
/// keeping the eligibility decision itself pure and independently testable.
/// </summary>
public sealed record CloudAccountLinkRequest
{
    public uint MainAccountId { get; }

    public uint SourceAccountId { get; }

    /// <summary>True when the proposed Main Account is itself a Linked Account elsewhere.</summary>
    public bool MainAccountIsLinkedElsewhere { get; }

    /// <summary>True when the source account is already a Linked Account of some Main Account.</summary>
    public bool SourceIsAlreadyLinked { get; }

    /// <summary>True when the source account is itself a Main Account with its own Linked Accounts.</summary>
    public bool SourceHasLinkedAccounts { get; }

    /// <summary>
    /// True when the source account holds any active reservation, listing, bid, settlement,
    /// Withdrawal Token, Transfer Offer, or other in-flight obligation.
    /// </summary>
    public bool SourceHasPendingObligations { get; }

    /// <summary>True when linking would create a seller/bidder self-dealing conflict in an active auction.</summary>
    public bool WouldCreateActiveAuctionConflict { get; }

    public CloudMutationGateState MutationGateState { get; }

    public CloudAccountLinkRequest(
        uint mainAccountId,
        uint sourceAccountId,
        bool mainAccountIsLinkedElsewhere,
        bool sourceIsAlreadyLinked,
        bool sourceHasLinkedAccounts,
        bool sourceHasPendingObligations,
        bool wouldCreateActiveAuctionConflict,
        CloudMutationGateState mutationGateState)
    {
        if (mainAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mainAccountId), "An account link request requires a real Main Account ID.");
        }

        if (sourceAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAccountId), "An account link request requires a real source account ID.");
        }

        MainAccountId = mainAccountId;
        SourceAccountId = sourceAccountId;
        MainAccountIsLinkedElsewhere = mainAccountIsLinkedElsewhere;
        SourceIsAlreadyLinked = sourceIsAlreadyLinked;
        SourceHasLinkedAccounts = sourceHasLinkedAccounts;
        SourceHasPendingObligations = sourceHasPendingObligations;
        WouldCreateActiveAuctionConflict = wouldCreateActiveAuctionConflict;
        MutationGateState = mutationGateState;
    }
}
