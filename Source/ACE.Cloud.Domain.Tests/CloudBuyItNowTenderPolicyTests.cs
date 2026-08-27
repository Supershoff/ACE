namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Coverage for <see cref="CloudBuyItNowTenderPolicy"/> (MKT-108, MKT-109; issue #9 Red section:
/// "Buy It Now has only an overpaying tender"). Buy It Now is the only place the tender/proxy
/// engines ever propose spending more than the advertised price, and only as an explicitly
/// disclosed, buyer-confirmed fallback.
/// </summary>
[TestClass]
public sealed class CloudBuyItNowTenderPolicyTests
{
    private static CloudReservationTarget Lot(Guid guid) => CloudReservationTarget.ForStackLot(new CloudStackLotId(guid));

    private static CloudAuthorizedPaymentMix SingleRowMix(long unitValue, long quantity) =>
        new([new CloudCurrencyPaymentRow(1, unitValue, 0, [new CloudCurrencyAsset(Lot(Guid.NewGuid()), quantity)])]);

    [TestMethod]
    public void Preview_ExactTenderAvailable_PrefersItAndNeedsNoConfirmation()
    {
        var mix = SingleRowMix(unitValue: 10, quantity: 10);

        var preview = CloudBuyItNowTenderPolicy.Preview(mix, advertisedPriceUnits: 50);

        Assert.AreEqual(CloudBuyItNowTenderOutcomeKind.Exact, preview.Kind);
        Assert.AreEqual(50L, preview.TenderedPriceUnits);
        Assert.AreEqual(0L, preview.ExcessUnits);
    }

    [TestMethod]
    public void Preview_OnlyOverpayingTenderExists_DisclosesExactExcessAndRequiresConfirmation()
    {
        // Only 25-Unit denominations are authorized; a 60-Unit asking price cannot be composed
        // exactly, but 75 can.
        var mix = SingleRowMix(unitValue: 25, quantity: 10);

        var preview = CloudBuyItNowTenderPolicy.Preview(mix, advertisedPriceUnits: 60);

        Assert.AreEqual(CloudBuyItNowTenderOutcomeKind.RequiresOverpaymentConfirmation, preview.Kind);
        Assert.AreEqual(60L, preview.AdvertisedPriceUnits);
        Assert.AreEqual(75L, preview.TenderedPriceUnits);
        Assert.AreEqual(15L, preview.ExcessUnits);
        Assert.IsNotEmpty(preview.Lines);
    }

    [TestMethod]
    public void Preview_NoAuthorizedCurrencyCanReachThePrice_ReportsNoTenderAvailable()
    {
        var mix = SingleRowMix(unitValue: 25, quantity: 2); // total available value = 50

        var preview = CloudBuyItNowTenderPolicy.Preview(mix, advertisedPriceUnits: 60);

        Assert.AreEqual(CloudBuyItNowTenderOutcomeKind.NoTenderAvailable, preview.Kind);
        Assert.IsNull(preview.TenderedPriceUnits);
    }

    [TestMethod]
    public void Preview_NonPositiveAdvertisedPrice_Throws()
    {
        var mix = SingleRowMix(unitValue: 25, quantity: 2);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CloudBuyItNowTenderPolicy.Preview(mix, advertisedPriceUnits: 0));
    }
}
