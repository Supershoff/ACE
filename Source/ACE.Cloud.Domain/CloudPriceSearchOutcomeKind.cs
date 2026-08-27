namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudExactTenderEngine.FindSmallestExactlyPayableAtOrAbove"/>
/// (MKT-103's Proxy Increment search, MKT-108's Buy It Now overpayment search).
/// </summary>
public enum CloudPriceSearchOutcomeKind
{
    /// <summary>The smallest exactly payable price in range was found.</summary>
    Found,

    /// <summary>No exactly payable price exists anywhere in the requested range.</summary>
    NotFound,

    /// <summary>The search exceeded the engine's bounded-complexity limit.</summary>
    SearchBoundExceeded,
}
