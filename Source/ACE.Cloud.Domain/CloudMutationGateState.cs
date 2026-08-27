namespace ACE.Cloud.Domain;

/// <summary>
/// Whether custody/reservation mutations are currently permitted to commit (transaction rule 9:
/// "Treat Global/Marketplace freezes as transaction preconditions revalidated at commit, not only UI
/// flags."). Global Cloud Maintenance and Marketplace State are full administrative aggregates out
/// of scope for this issue; a caller resolves its own current gate from whichever of those applies
/// and passes the result here so <see cref="CloudReservationPolicy"/> and
/// <see cref="CloudOwnershipTransferPolicy"/> revalidate it at the exact instant they also check
/// version and exclusivity, not only earlier in the request pipeline.
/// </summary>
public enum CloudMutationGateState
{
    /// <summary>Ordinary mutations are permitted.</summary>
    Open,

    /// <summary>Every custody/reservation mutation must be refused until the freeze lifts.</summary>
    Frozen,
}
