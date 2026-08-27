namespace ACE.Cloud.Domain;

/// <summary>
/// The result of searching an Authorized Payment Mix for the smallest exactly payable price within
/// a range (MKT-103's Proxy Increment, MKT-108's Buy It Now overpayment search).
/// </summary>
public sealed record CloudPriceSearchResult
{
    public CloudPriceSearchOutcomeKind Kind { get; }

    public long? PriceUnits { get; }

    private CloudPriceSearchResult(CloudPriceSearchOutcomeKind kind, long? priceUnits)
    {
        Kind = kind;
        PriceUnits = priceUnits;
    }

    public static CloudPriceSearchResult Found(long priceUnits) => new(CloudPriceSearchOutcomeKind.Found, priceUnits);

    public static CloudPriceSearchResult NotFound() => new(CloudPriceSearchOutcomeKind.NotFound, null);

    public static CloudPriceSearchResult SearchBoundExceeded() => new(CloudPriceSearchOutcomeKind.SearchBoundExceeded, null);
}
