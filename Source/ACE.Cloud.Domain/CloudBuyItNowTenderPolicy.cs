namespace ACE.Cloud.Domain;

/// <summary>
/// The Buy It Now tender policy (MKT-108, MKT-109): prefers an exact tender at the advertised
/// price and only ever offers a disclosed, buyer-confirmed overpayment as a fallback -- proxy bids
/// and normal auction settlement never overpay (MKT-109: "Buy It Now Overpayment is the only
/// intentional overpayment path").
/// </summary>
public static class CloudBuyItNowTenderPolicy
{
    public static CloudBuyItNowTenderPreview Preview(CloudAuthorizedPaymentMix buyerPaymentMix, long advertisedPriceUnits)
    {
        ArgumentNullException.ThrowIfNull(buyerPaymentMix);

        if (advertisedPriceUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(advertisedPriceUnits), "An advertised Buy It Now price must be positive.");
        }

        var exact = CloudExactTenderEngine.TrySelectExactTender(buyerPaymentMix, advertisedPriceUnits);
        if (exact.IsComposed)
        {
            return CloudBuyItNowTenderPreview.Exact(advertisedPriceUnits, exact.Lines);
        }

        if (exact.Kind == CloudTenderOutcomeKind.PriceExceedsSearchBound)
        {
            return CloudBuyItNowTenderPreview.SearchBoundExceeded(advertisedPriceUnits);
        }

        var search = CloudExactTenderEngine.FindSmallestExactlyPayableAtOrAbove(buyerPaymentMix, advertisedPriceUnits, ceilingInclusive: null);

        switch (search.Kind)
        {
            case CloudPriceSearchOutcomeKind.SearchBoundExceeded:
                return CloudBuyItNowTenderPreview.SearchBoundExceeded(advertisedPriceUnits);
            case CloudPriceSearchOutcomeKind.NotFound:
                return CloudBuyItNowTenderPreview.NoTenderAvailable(advertisedPriceUnits);
        }

        // FindSmallestExactlyPayableAtOrAbove only ever returns a price it has already proven
        // reachable, so this second composition can never itself report NoExactTenderExists.
        var overpayTender = CloudExactTenderEngine.TrySelectExactTender(buyerPaymentMix, search.PriceUnits!.Value);
        return CloudBuyItNowTenderPreview.RequiresOverpaymentConfirmation(advertisedPriceUnits, search.PriceUnits.Value, overpayTender.Lines);
    }
}
