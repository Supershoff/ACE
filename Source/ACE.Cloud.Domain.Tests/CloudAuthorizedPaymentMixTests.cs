namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Construction-time validation for the Authorized Payment Mix building blocks (MKT-101, MKT-102;
/// issue #9 Red section: "duplicate authorized assets" and "one asset backing two obligations").
/// Every downstream tender/priority/proxy engine assumes a mix that already passed these checks.
/// </summary>
[TestClass]
public sealed class CloudAuthorizedPaymentMixTests
{
    private static CloudReservationTarget Item(uint guid) => CloudReservationTarget.ForItem(new CloudItemId(guid));

    private static CloudReservationTarget Lot(Guid guid) => CloudReservationTarget.ForStackLot(new CloudStackLotId(guid));

    [TestMethod]
    public void CurrencyAsset_NonStackableItemWithQuantityOtherThanOne_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudCurrencyAsset(Item(1), 2));
    }

    [TestMethod]
    public void CurrencyAsset_NonPositiveQuantity_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudCurrencyAsset(Lot(Guid.NewGuid()), 0));
    }

    [TestMethod]
    public void CurrencyAsset_StackLotWithPositiveQuantity_Succeeds()
    {
        var asset = new CloudCurrencyAsset(Lot(Guid.NewGuid()), 5);
        Assert.AreEqual(5, asset.AvailableQuantity);
    }

    [TestMethod]
    public void PaymentRow_NonPositiveUnitValue_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new CloudCurrencyPaymentRow(1, 0, 0, [new CloudCurrencyAsset(Item(1), 1)]));
    }

    [TestMethod]
    public void PaymentRow_NoAssets_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CloudCurrencyPaymentRow(1, 100, 0, []));
    }

    [TestMethod]
    public void PaymentRow_DuplicateAssetTargetWithinOneRow_IsRejected()
    {
        var target = Item(1);
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CloudCurrencyPaymentRow(1, 100, 0, [new CloudCurrencyAsset(target, 1), new CloudCurrencyAsset(target, 1)]));
    }

    [TestMethod]
    public void PaymentRow_TotalQuantityOverflow_IsRejected()
    {
        var assets = new[]
        {
            new CloudCurrencyAsset(Lot(Guid.NewGuid()), long.MaxValue),
            new CloudCurrencyAsset(Lot(Guid.NewGuid()), long.MaxValue),
        };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudCurrencyPaymentRow(1, 1, 0, assets));
    }

    [TestMethod]
    public void PaymentRow_TotalValueOverflow_IsRejected()
    {
        var assets = new[] { new CloudCurrencyAsset(Lot(Guid.NewGuid()), long.MaxValue) };

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudCurrencyPaymentRow(1, 2, 0, assets));
    }

    [TestMethod]
    public void PaymentRow_TotalAvailableQuantity_SumsAcrossAssets()
    {
        var row = new CloudCurrencyPaymentRow(
            1, 100, 0, [new CloudCurrencyAsset(Lot(Guid.NewGuid()), 3), new CloudCurrencyAsset(Lot(Guid.NewGuid()), 4)]);

        Assert.AreEqual(7, row.TotalAvailableQuantity);
        Assert.AreEqual(700, row.TotalContributableUnits);
    }

    private static CloudCurrencyPaymentRow SimpleRow(int wcid, long unitValue, int priorityRank, uint itemGuid) =>
        new(wcid, unitValue, priorityRank, [new CloudCurrencyAsset(Item(itemGuid), 1)]);

    [TestMethod]
    public void Mix_NoRows_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CloudAuthorizedPaymentMix([]));
    }

    [TestMethod]
    public void Mix_DuplicateWcidAcrossRows_IsRejected()
    {
        var rows = new[] { SimpleRow(1, 100, 0, 1), SimpleRow(1, 200, 1, 2) };
        Assert.ThrowsExactly<ArgumentException>(() => new CloudAuthorizedPaymentMix(rows));
    }

    [TestMethod]
    public void Mix_DuplicatePriorityRankAcrossRows_IsRejected()
    {
        var rows = new[] { SimpleRow(1, 100, 0, 1), SimpleRow(2, 200, 0, 2) };
        Assert.ThrowsExactly<ArgumentException>(() => new CloudAuthorizedPaymentMix(rows));
    }

    [TestMethod]
    public void Mix_SameAssetBackingTwoDifferentRows_IsRejectedAsOneAssetBackingTwoObligations()
    {
        var sharedTarget = Item(1);
        var rows = new[]
        {
            new CloudCurrencyPaymentRow(1, 100, 0, [new CloudCurrencyAsset(sharedTarget, 1)]),
            new CloudCurrencyPaymentRow(2, 200, 1, [new CloudCurrencyAsset(sharedTarget, 1)]),
        };

        Assert.ThrowsExactly<ArgumentException>(() => new CloudAuthorizedPaymentMix(rows));
    }

    [TestMethod]
    public void Mix_ValidRows_OrdersSpendingOrderByPriorityRank()
    {
        var rows = new[] { SimpleRow(1, 100, 2, 1), SimpleRow(2, 200, 0, 2), SimpleRow(3, 300, 1, 3) };
        var mix = new CloudAuthorizedPaymentMix(rows);

        CollectionAssert.AreEqual(new[] { 2, 3, 1 }, mix.SpendingOrder.Select(r => r.Wcid).ToList());
    }
}
