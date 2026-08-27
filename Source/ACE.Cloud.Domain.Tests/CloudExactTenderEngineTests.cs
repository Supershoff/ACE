namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Coverage for <see cref="CloudExactTenderEngine"/> (MKT-001, MKT-006, MKT-101, MKT-102, MKT-106;
/// issue #9 Red section): exact subset/quantity selection across denominations, stack quantities,
/// WCID priority, deterministic GUID order, equal maxima are covered by
/// <see cref="CloudBidPriorityPolicyTests"/>; this file covers no-exact-tender cases, overflow/
/// bounded-complexity behavior, escrow release, and a randomized brute-force oracle comparison
/// (this issue's acceptance criterion: "Randomized oracle comparison confirms minimal exactly
/// payable prices and preferred tender selection").
/// </summary>
[TestClass]
public sealed class CloudExactTenderEngineTests
{
    private static CloudReservationTarget Item(uint guid) => CloudReservationTarget.ForItem(new CloudItemId(guid));

    private static CloudReservationTarget Lot(Guid guid) => CloudReservationTarget.ForStackLot(new CloudStackLotId(guid));

    private static CloudCurrencyPaymentRow Row(int wcid, long unitValue, int priorityRank, params CloudCurrencyAsset[] assets) =>
        new(wcid, unitValue, priorityRank, assets);

    [TestMethod]
    public void TrySelectExactTender_NonPositivePrice_Throws()
    {
        var mix = new CloudAuthorizedPaymentMix([Row(1, 10, 0, new CloudCurrencyAsset(Item(1), 1))]);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CloudExactTenderEngine.TrySelectExactTender(mix, 0));
    }

    [TestMethod]
    public void TrySelectExactTender_ExactSingleDenominationMatch_Composes()
    {
        // Two 10-Unit Trade Notes exactly compose a 20-Unit price.
        var mix = new CloudAuthorizedPaymentMix(
        [
            Row(1, 10, 0, new CloudCurrencyAsset(Lot(Guid.NewGuid()), 5)),
        ]);

        var result = CloudExactTenderEngine.TrySelectExactTender(mix, 20);

        Assert.AreEqual(CloudTenderOutcomeKind.Composed, result.Kind);
        Assert.AreEqual(20L, result.PriceUnits);
        Assert.HasCount(1, result.Lines);
        Assert.AreEqual(2L, result.Lines[0].QuantitySpent);
        Assert.AreEqual(20L, result.Lines[0].UnitsContributed);
    }

    [TestMethod]
    public void TrySelectExactTender_PriceNotAMultipleOfAnyDenominationGcd_ReportsNoExactTender()
    {
        var mix = new CloudAuthorizedPaymentMix(
        [
            Row(1, 10, 0, new CloudCurrencyAsset(Lot(Guid.NewGuid()), 5)),
            Row(2, 25, 1, new CloudCurrencyAsset(Lot(Guid.NewGuid()), 4)),
        ]);

        // gcd(10, 25) = 5; 53 is not a multiple of 5.
        var result = CloudExactTenderEngine.TrySelectExactTender(mix, 53);

        Assert.AreEqual(CloudTenderOutcomeKind.NoExactTenderExists, result.Kind);
        Assert.IsEmpty(result.Lines);
    }

    [TestMethod]
    public void TrySelectExactTender_PriceAboveTotalAvailableValue_ReportsNoExactTender()
    {
        var mix = new CloudAuthorizedPaymentMix([Row(1, 10, 0, new CloudCurrencyAsset(Item(1), 1))]);

        var result = CloudExactTenderEngine.TrySelectExactTender(mix, 1_000);

        Assert.AreEqual(CloudTenderOutcomeKind.NoExactTenderExists, result.Kind);
    }

    [TestMethod]
    public void TrySelectExactTender_PreferHigherPriorityWcid_MaximizesItsUsageBeforeTouchingLowerPriority()
    {
        // Priority 0 = 5-Unit MMDs (8 available); priority 1 = 3-Unit Notes (10 available).
        // Target 34 is composable by MMDs alone with a remainder handled by Notes: 6*5=30 + 3-unit note*? no.
        // Choose target so the greedy split is unambiguous: 34 = 5*c0 + 3*c1, maximize c0 (<=8).
        // c0=8 -> remainder 34-40 <0; c0=6 -> remainder 4, not divisible by 3; c0=5 -> remainder 9 = 3*3 (valid, c1=3<=10).
        var mmds = new CloudCurrencyAsset(Lot(Guid.NewGuid()), 8);
        var notes = new CloudCurrencyAsset(Lot(Guid.NewGuid()), 10);
        var mix = new CloudAuthorizedPaymentMix([Row(1, 5, 0, mmds), Row(2, 3, 1, notes)]);

        var result = CloudExactTenderEngine.TrySelectExactTender(mix, 34);

        Assert.AreEqual(CloudTenderOutcomeKind.Composed, result.Kind);
        var byWcid = result.Lines.ToDictionary(l => l.Wcid, l => l.QuantitySpent);
        Assert.AreEqual(5L, byWcid[1]);
        Assert.AreEqual(3L, byWcid[2]);
    }

    [TestMethod]
    public void TrySelectExactTender_WithinOneWcid_ConsumesAssetsInDeterministicGuidOrder()
    {
        var lowGuidTarget = Item(1);
        var highGuidTarget = Item(2);

        // Two 10-Unit non-stackable items of the same WCID; a 10-Unit price should spend exactly the
        // deterministically first-ordered target (matching CloudReservationTargetOrdering).
        var mix = new CloudAuthorizedPaymentMix(
        [
            Row(1, 10, 0, new CloudCurrencyAsset(highGuidTarget, 1), new CloudCurrencyAsset(lowGuidTarget, 1)),
        ]);

        var result = CloudExactTenderEngine.TrySelectExactTender(mix, 10);

        Assert.AreEqual(CloudTenderOutcomeKind.Composed, result.Kind);
        Assert.HasCount(1, result.Lines);
        Assert.AreEqual(lowGuidTarget, result.Lines[0].Target);
    }

    [TestMethod]
    public void TrySelectExactTender_PriceExceedingSearchBound_ReportsBoundExceeded()
    {
        // unit value 1 with a huge available quantity: gcd = 1, so scaledPrice == priceUnits.
        var mix = new CloudAuthorizedPaymentMix([Row(1, 1, 0, new CloudCurrencyAsset(Lot(Guid.NewGuid()), 10_000_000))]);

        var result = CloudExactTenderEngine.TrySelectExactTender(mix, CloudExactTenderEngine.MaxScaledSearchSpan + 1);

        Assert.AreEqual(CloudTenderOutcomeKind.PriceExceedsSearchBound, result.Kind);
    }

    [TestMethod]
    public void FindSmallestExactlyPayableAtOrAbove_SkipsUnreachableAmountsUpToNextDenominationMultiple()
    {
        // Only a single 25-Unit denomination is authorized; the smallest exactly payable amount at or
        // above 51 is 75, a jump of more than one Unit (MKT-103's denomination-forced jump).
        var mix = new CloudAuthorizedPaymentMix([Row(1, 25, 0, new CloudCurrencyAsset(Lot(Guid.NewGuid()), 10))]);

        var result = CloudExactTenderEngine.FindSmallestExactlyPayableAtOrAbove(mix, 51, ceilingInclusive: null);

        Assert.AreEqual(CloudPriceSearchOutcomeKind.Found, result.Kind);
        Assert.AreEqual(75L, result.PriceUnits);
    }

    [TestMethod]
    public void FindSmallestExactlyPayableAtOrAbove_NoReachableAmountWithinCeiling_ReportsNotFound()
    {
        var mix = new CloudAuthorizedPaymentMix([Row(1, 25, 0, new CloudCurrencyAsset(Lot(Guid.NewGuid()), 10))]);

        var result = CloudExactTenderEngine.FindSmallestExactlyPayableAtOrAbove(mix, 51, ceilingInclusive: 74);

        Assert.AreEqual(CloudPriceSearchOutcomeKind.NotFound, result.Kind);
    }

    [TestMethod]
    public void FindSmallestExactlyPayableAtOrAbove_CeilingBelowFloor_Throws()
    {
        var mix = new CloudAuthorizedPaymentMix([Row(1, 25, 0, new CloudCurrencyAsset(Lot(Guid.NewGuid()), 10))]);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CloudExactTenderEngine.FindSmallestExactlyPayableAtOrAbove(mix, 100, ceilingInclusive: 50));
    }

    [TestMethod]
    public void ComputeUnusedPortion_ForAPartiallySpentMix_ReturnsExactlyTheRemainingQuantities()
    {
        var stackTarget = Lot(Guid.NewGuid());
        var mix = new CloudAuthorizedPaymentMix([Row(1, 10, 0, new CloudCurrencyAsset(stackTarget, 5))]);

        var tender = CloudExactTenderEngine.TrySelectExactTender(mix, 20);
        var unused = CloudExactTenderEngine.ComputeUnusedPortion(mix, tender);

        Assert.HasCount(1, unused);
        Assert.AreEqual(stackTarget, unused[0].Target);
        Assert.AreEqual(3L, unused[0].AvailableQuantity);
    }

    [TestMethod]
    public void ComputeUnusedPortion_ForAnUncomposedTender_Throws()
    {
        var mix = new CloudAuthorizedPaymentMix([Row(1, 10, 0, new CloudCurrencyAsset(Item(1), 1))]);
        var tender = CloudExactTenderEngine.TrySelectExactTender(mix, 999);

        Assert.ThrowsExactly<ArgumentException>(() => CloudExactTenderEngine.ComputeUnusedPortion(mix, tender));
    }

    // ---- Randomized brute-force oracle comparison -------------------------------------------------

    private readonly record struct OracleRow(long UnitValue, long Available);

    private static bool BruteForceReachable(IReadOnlyList<OracleRow> rows, int fromIndex, long amount)
    {
        if (amount == 0)
        {
            return true;
        }

        if (amount < 0 || fromIndex == rows.Count)
        {
            return false;
        }

        var row = rows[fromIndex];
        var maxCount = Math.Min(row.Available, amount / row.UnitValue);
        for (var c = 0; c <= maxCount; c++)
        {
            if (BruteForceReachable(rows, fromIndex + 1, amount - c * row.UnitValue))
            {
                return true;
            }
        }

        return false;
    }

    private static long[]? BruteForceGreedyComposition(IReadOnlyList<OracleRow> rowsInPriorityOrder, long price)
    {
        if (!BruteForceReachable(rowsInPriorityOrder, 0, price))
        {
            return null;
        }

        var chosen = new long[rowsInPriorityOrder.Count];
        var remaining = price;
        for (var i = 0; i < rowsInPriorityOrder.Count; i++)
        {
            var row = rowsInPriorityOrder[i];
            var maxCount = Math.Min(row.Available, remaining / row.UnitValue);
            for (var c = maxCount; c >= 0; c--)
            {
                if (BruteForceReachable(rowsInPriorityOrder, i + 1, remaining - c * row.UnitValue))
                {
                    chosen[i] = c;
                    remaining -= c * row.UnitValue;
                    break;
                }
            }
        }

        return chosen;
    }

    [TestMethod]
    public void TrySelectExactTender_MatchesBruteForceOracle_AcrossRandomizedSmallScenarios()
    {
        var random = new Random(20260827);

        for (var trial = 0; trial < 300; trial++)
        {
            var rowCount = random.Next(1, 4);
            var oracleRows = new List<OracleRow>();
            var paymentRows = new List<CloudCurrencyPaymentRow>();

            for (var i = 0; i < rowCount; i++)
            {
                var unitValue = random.Next(1, 8);
                var assetCount = random.Next(1, 3);
                var assets = new List<CloudCurrencyAsset>();
                long available = 0;

                for (var a = 0; a < assetCount; a++)
                {
                    var quantity = random.Next(1, 5);
                    available += quantity;
                    assets.Add(new CloudCurrencyAsset(Lot(Guid.NewGuid()), quantity));
                }

                oracleRows.Add(new OracleRow(unitValue, available));
                paymentRows.Add(new CloudCurrencyPaymentRow(i + 1, unitValue, i, assets));
            }

            var mix = new CloudAuthorizedPaymentMix(paymentRows);
            var totalValue = oracleRows.Sum(r => r.UnitValue * r.Available);
            var price = random.Next(1, (int)totalValue + 4);

            var expected = BruteForceGreedyComposition(oracleRows, price);
            var actual = CloudExactTenderEngine.TrySelectExactTender(mix, price);

            if (expected is null)
            {
                Assert.AreEqual(CloudTenderOutcomeKind.NoExactTenderExists, actual.Kind, $"trial {trial}, price {price}");
                continue;
            }

            Assert.AreEqual(CloudTenderOutcomeKind.Composed, actual.Kind, $"trial {trial}, price {price}");

            var actualByWcid = actual.Lines
                .GroupBy(l => l.Wcid)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.QuantitySpent));

            for (var i = 0; i < expected.Length; i++)
            {
                var wcid = i + 1;
                var actualQuantity = actualByWcid.GetValueOrDefault(wcid, 0L);
                Assert.AreEqual(expected[i], actualQuantity, $"trial {trial}, price {price}, row {i}");
            }
        }
    }

    [TestMethod]
    public void FindSmallestExactlyPayableAtOrAbove_MatchesBruteForceOracle_AcrossRandomizedSmallScenarios()
    {
        var random = new Random(918234);

        for (var trial = 0; trial < 150; trial++)
        {
            var rowCount = random.Next(1, 3);
            var oracleRows = new List<OracleRow>();
            var paymentRows = new List<CloudCurrencyPaymentRow>();

            for (var i = 0; i < rowCount; i++)
            {
                var unitValue = random.Next(2, 9);
                var quantity = random.Next(1, 6);
                oracleRows.Add(new OracleRow(unitValue, quantity));
                paymentRows.Add(new CloudCurrencyPaymentRow(i + 1, unitValue, i, [new CloudCurrencyAsset(Lot(Guid.NewGuid()), quantity)]));
            }

            var mix = new CloudAuthorizedPaymentMix(paymentRows);
            var totalValue = oracleRows.Sum(r => r.UnitValue * r.Available);
            var floor = random.Next(1, (int)totalValue + 4);

            long? expected = null;
            for (var amount = floor; amount <= totalValue; amount++)
            {
                if (BruteForceReachable(oracleRows, 0, amount))
                {
                    expected = amount;
                    break;
                }
            }

            var actual = CloudExactTenderEngine.FindSmallestExactlyPayableAtOrAbove(mix, floor, ceilingInclusive: null);

            if (expected is null)
            {
                Assert.AreEqual(CloudPriceSearchOutcomeKind.NotFound, actual.Kind, $"trial {trial}, floor {floor}");
            }
            else
            {
                Assert.AreEqual(CloudPriceSearchOutcomeKind.Found, actual.Kind, $"trial {trial}, floor {floor}");
                Assert.AreEqual(expected, actual.PriceUnits, $"trial {trial}, floor {floor}");
            }
        }
    }
}
