namespace ACE.Cloud.Domain;

/// <summary>
/// The Proxy Increment engine's immutable result (MKT-103): the smallest exactly payable price
/// above the competing price and within the bidder's maximum, plus whether reaching it required a
/// denomination-forced jump of more than one Unit (MKT-103: "A proxy price may visibly jump by more
/// than one Unit only when the bidder's authorized physical currency denominations cannot pay an
/// intermediate value exactly" -- the bidder must confirm that disclosed jump before it applies).
/// </summary>
public sealed record CloudProxyBidResult
{
    public CloudProxyBidOutcomeKind Kind { get; }

    public long? PriceUnits { get; }

    public bool RequiresDenominationJumpDisclosure { get; }

    private CloudProxyBidResult(CloudProxyBidOutcomeKind kind, long? priceUnits, bool requiresDenominationJumpDisclosure)
    {
        Kind = kind;
        PriceUnits = priceUnits;
        RequiresDenominationJumpDisclosure = requiresDenominationJumpDisclosure;
    }

    public static CloudProxyBidResult Determined(long priceUnits, long competingPriceUnits) =>
        new(CloudProxyBidOutcomeKind.Determined, priceUnits, priceUnits - competingPriceUnits > 1);

    public static CloudProxyBidResult NoPriceWithinMaximum() => new(CloudProxyBidOutcomeKind.NoPriceWithinMaximum, null, false);

    public static CloudProxyBidResult SearchBoundExceeded() => new(CloudProxyBidOutcomeKind.SearchBoundExceeded, null, false);
}
