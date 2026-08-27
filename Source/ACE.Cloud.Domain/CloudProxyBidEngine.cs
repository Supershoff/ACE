namespace ACE.Cloud.Domain;

/// <summary>
/// The Proxy Increment engine (MKT-103, MKT-104): computes the smallest exactly payable public
/// price above the current competing price/opening price and within the bidder's own maximum,
/// using only the bidder's own Authorized Payment Mix -- it never manufactures change and never
/// proposes a price above the bidder's private maximum. Also validates the Binding Bid Floor
/// (MKT-104): the current leader may reduce their private maximum no lower than the current public
/// price.
/// </summary>
public static class CloudProxyBidEngine
{
    /// <summary>
    /// <paramref name="competingPriceUnits"/> is the current public price to beat: either the
    /// second-highest bidder's exactly payable price, or the listing's Opening Price when this is
    /// the first bid. <paramref name="leaderMaxUnits"/> is this bidder's own accepted maximum, and
    /// <paramref name="leaderPaymentMix"/> is the same bidder's Authorized Payment Mix.
    /// </summary>
    public static CloudProxyBidResult ComputeNextProxyPrice(
        long competingPriceUnits, long leaderMaxUnits, CloudAuthorizedPaymentMix leaderPaymentMix)
    {
        ArgumentNullException.ThrowIfNull(leaderPaymentMix);

        if (competingPriceUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(competingPriceUnits), "A competing price must be positive.");
        }

        if (leaderMaxUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(leaderMaxUnits), "A bid maximum must be positive.");
        }

        long floor;
        try
        {
            floor = checked(competingPriceUnits + 1);
        }
        catch (OverflowException)
        {
            return CloudProxyBidResult.NoPriceWithinMaximum();
        }

        if (floor > leaderMaxUnits)
        {
            return CloudProxyBidResult.NoPriceWithinMaximum();
        }

        var search = CloudExactTenderEngine.FindSmallestExactlyPayableAtOrAbove(leaderPaymentMix, floor, leaderMaxUnits);

        return search.Kind switch
        {
            CloudPriceSearchOutcomeKind.Found => CloudProxyBidResult.Determined(search.PriceUnits!.Value, competingPriceUnits),
            CloudPriceSearchOutcomeKind.NotFound => CloudProxyBidResult.NoPriceWithinMaximum(),
            CloudPriceSearchOutcomeKind.SearchBoundExceeded => CloudProxyBidResult.SearchBoundExceeded(),
            _ => throw new ArgumentOutOfRangeException(nameof(search), "Unrecognized price search outcome kind."),
        };
    }

    /// <summary>
    /// MKT-104's Binding Bid Floor: the current leader may reduce their private maximum no lower
    /// than the current public price, without lowering the public price or changing the winner.
    /// </summary>
    public static bool IsAboveBindingBidFloor(long currentPublicPriceUnits, long requestedNewMaxUnits)
    {
        if (currentPublicPriceUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentPublicPriceUnits), "A public price must be positive.");
        }

        if (requestedNewMaxUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedNewMaxUnits), "A requested maximum must be positive.");
        }

        return requestedNewMaxUnits >= currentPublicPriceUnits;
    }
}
