namespace ACE.Cloud.Domain;

/// <summary>
/// The Buy It Now tender preview (MKT-108, MKT-109): prefers an exact tender; when none exists,
/// discloses the smallest authorized overpayment and its exact excess for the buyer to explicitly
/// confirm before any change-free overpaying settlement (Buy It Now Overpayment is the only
/// intentional overpayment path anywhere in the marketplace).
/// </summary>
public sealed record CloudBuyItNowTenderPreview
{
    public CloudBuyItNowTenderOutcomeKind Kind { get; }

    public long AdvertisedPriceUnits { get; }

    public long? TenderedPriceUnits { get; }

    public long ExcessUnits => TenderedPriceUnits is null ? 0 : TenderedPriceUnits.Value - AdvertisedPriceUnits;

    public IReadOnlyList<CloudTenderLine> Lines { get; }

    private CloudBuyItNowTenderPreview(
        CloudBuyItNowTenderOutcomeKind kind, long advertisedPriceUnits, long? tenderedPriceUnits, IReadOnlyList<CloudTenderLine> lines)
    {
        Kind = kind;
        AdvertisedPriceUnits = advertisedPriceUnits;
        TenderedPriceUnits = tenderedPriceUnits;
        Lines = lines;
    }

    public static CloudBuyItNowTenderPreview Exact(long advertisedPriceUnits, IReadOnlyList<CloudTenderLine> lines) =>
        new(CloudBuyItNowTenderOutcomeKind.Exact, advertisedPriceUnits, advertisedPriceUnits, lines);

    public static CloudBuyItNowTenderPreview RequiresOverpaymentConfirmation(
        long advertisedPriceUnits, long tenderedPriceUnits, IReadOnlyList<CloudTenderLine> lines) =>
        new(CloudBuyItNowTenderOutcomeKind.RequiresOverpaymentConfirmation, advertisedPriceUnits, tenderedPriceUnits, lines);

    public static CloudBuyItNowTenderPreview NoTenderAvailable(long advertisedPriceUnits) =>
        new(CloudBuyItNowTenderOutcomeKind.NoTenderAvailable, advertisedPriceUnits, null, []);

    public static CloudBuyItNowTenderPreview SearchBoundExceeded(long advertisedPriceUnits) =>
        new(CloudBuyItNowTenderOutcomeKind.SearchBoundExceeded, advertisedPriceUnits, null, []);
}
