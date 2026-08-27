namespace ACE.Cloud.Domain;

/// <summary>The outcome of <see cref="CloudProxyBidEngine.ComputeNextProxyPrice"/> (MKT-103).</summary>
public enum CloudProxyBidOutcomeKind
{
    /// <summary>A new public price was determined.</summary>
    Determined,

    /// <summary>
    /// The bidder's authorized payment mix cannot exactly compose any price above the competing
    /// price within their own maximum; this bidder cannot currently take the lead.
    /// </summary>
    NoPriceWithinMaximum,

    /// <summary>The search exceeded <see cref="CloudExactTenderEngine.MaxScaledSearchSpan"/>.</summary>
    SearchBoundExceeded,
}
