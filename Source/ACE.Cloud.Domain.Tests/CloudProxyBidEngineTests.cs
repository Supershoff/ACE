namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Coverage for <see cref="CloudProxyBidEngine"/> (MKT-103, MKT-104; issue #9 Red section:
/// "the next payable proxy jump exceeds one Unit" and "binding-floor reductions").
/// </summary>
[TestClass]
public sealed class CloudProxyBidEngineTests
{
    private static CloudReservationTarget Lot(Guid guid) => CloudReservationTarget.ForStackLot(new CloudStackLotId(guid));

    private static CloudAuthorizedPaymentMix SingleRowMix(long unitValue, long quantity) =>
        new([new CloudCurrencyPaymentRow(1, unitValue, 0, [new CloudCurrencyAsset(Lot(Guid.NewGuid()), quantity)])]);

    [TestMethod]
    public void ComputeNextProxyPrice_OneUnitDenomination_AdvancesByExactlyOneUnitAboveCompeting()
    {
        var mix = SingleRowMix(unitValue: 1, quantity: 100);

        var result = CloudProxyBidEngine.ComputeNextProxyPrice(competingPriceUnits: 50, leaderMaxUnits: 100, mix);

        Assert.AreEqual(CloudProxyBidOutcomeKind.Determined, result.Kind);
        Assert.AreEqual(51L, result.PriceUnits);
        Assert.IsFalse(result.RequiresDenominationJumpDisclosure);
    }

    [TestMethod]
    public void ComputeNextProxyPrice_OnlyCoarseDenomination_JumpsMoreThanOneUnitAndRequiresDisclosure()
    {
        // Only 25-Unit denominations are authorized; the competing price is 51, so the smallest
        // exactly payable price above it is 75 -- a 24-Unit jump that must be disclosed.
        var mix = SingleRowMix(unitValue: 25, quantity: 10);

        var result = CloudProxyBidEngine.ComputeNextProxyPrice(competingPriceUnits: 51, leaderMaxUnits: 500, mix);

        Assert.AreEqual(CloudProxyBidOutcomeKind.Determined, result.Kind);
        Assert.AreEqual(75L, result.PriceUnits);
        Assert.IsTrue(result.RequiresDenominationJumpDisclosure);
    }

    [TestMethod]
    public void ComputeNextProxyPrice_NoExactlyPayablePriceWithinMaximum_ReportsNoPriceWithinMaximum()
    {
        var mix = SingleRowMix(unitValue: 25, quantity: 10);

        // Competing price is 51; the leader's maximum (60) is below the next payable 25-multiple (75).
        var result = CloudProxyBidEngine.ComputeNextProxyPrice(competingPriceUnits: 51, leaderMaxUnits: 60, mix);

        Assert.AreEqual(CloudProxyBidOutcomeKind.NoPriceWithinMaximum, result.Kind);
        Assert.IsNull(result.PriceUnits);
    }

    [TestMethod]
    public void ComputeNextProxyPrice_CompetingPriceAtMaximum_ReportsNoPriceWithinMaximum()
    {
        var mix = SingleRowMix(unitValue: 1, quantity: 10);

        var result = CloudProxyBidEngine.ComputeNextProxyPrice(competingPriceUnits: 10, leaderMaxUnits: 10, mix);

        Assert.AreEqual(CloudProxyBidOutcomeKind.NoPriceWithinMaximum, result.Kind);
    }

    [TestMethod]
    public void ComputeNextProxyPrice_NonPositiveCompetingPrice_Throws()
    {
        var mix = SingleRowMix(unitValue: 1, quantity: 10);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CloudProxyBidEngine.ComputeNextProxyPrice(competingPriceUnits: 0, leaderMaxUnits: 10, mix));
    }

    [TestMethod]
    public void IsAboveBindingBidFloor_AtOrAboveCurrentPublicPrice_IsAllowed()
    {
        Assert.IsTrue(CloudProxyBidEngine.IsAboveBindingBidFloor(currentPublicPriceUnits: 100, requestedNewMaxUnits: 100));
        Assert.IsTrue(CloudProxyBidEngine.IsAboveBindingBidFloor(currentPublicPriceUnits: 100, requestedNewMaxUnits: 150));
    }

    [TestMethod]
    public void IsAboveBindingBidFloor_BelowCurrentPublicPrice_IsRejected()
    {
        Assert.IsFalse(CloudProxyBidEngine.IsAboveBindingBidFloor(currentPublicPriceUnits: 100, requestedNewMaxUnits: 99));
    }
}
