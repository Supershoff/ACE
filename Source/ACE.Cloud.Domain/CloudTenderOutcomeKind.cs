namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of asking <see cref="CloudExactTenderEngine.TrySelectExactTender"/> to compose one
/// specific price (MKT-006, MKT-103, MKT-109: "the auction advances only to the smallest Exactly
/// Payable Bid, never manufactures change, and never silently overpays").
/// </summary>
public enum CloudTenderOutcomeKind
{
    /// <summary>The requested price was composed exactly, with no change.</summary>
    Composed,

    /// <summary>No combination of the authorized payment mix sums to the requested price exactly.</summary>
    NoExactTenderExists,

    /// <summary>
    /// The requested price is too large relative to the mix's denominations to search within the
    /// engine's bounded-complexity limit (<see cref="CloudExactTenderEngine.MaxScaledSearchSpan"/>).
    /// Not expected for realistic Marketplace prices and currency catalogs.
    /// </summary>
    PriceExceedsSearchBound,
}
