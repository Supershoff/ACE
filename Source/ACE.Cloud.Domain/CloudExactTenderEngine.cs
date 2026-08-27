namespace ACE.Cloud.Domain;

/// <summary>
/// The pure Exact Tender engine (MKT-001, MKT-006, MKT-101, MKT-102, MKT-106, MKT-109): selects the
/// actual authorized currency items/lots that exactly compose a requested price, never invents
/// change, and never spends above an authorized payment mix. Every method here is a pure function
/// over its inputs -- it never queries or mutates custody itself (issue #9 Green section: "do not
/// mutate custody inside the calculation engine"), matching <see cref="CloudReservationPolicy"/>'s
/// precedent of pure, independently testable Cloud domain policies.
///
/// Composition prefers the bidder's own drag-to-order spending priority: among every combination
/// that sums exactly to the requested price, it greedily maximizes usage of the highest-priority
/// currency row first, then the next, and so on (MKT-102: "consume higher-priority WCIDs first").
/// Within one currency row, it spends the deterministic <see cref="CloudReservationTargetOrdering"/>
/// order rather than reinventing GUID tie-breaking (MKT-102: "deterministic GUID ordering").
///
/// Exact subset-sum over arbitrary denominations is NP-hard in general, so this engine bounds its
/// own complexity in two ways instead of a naive 2^N subset search (issue #9 Green section: "bounded
/// complexity"): first, every reachable amount is a multiple of the greatest common divisor of the
/// mix's Unit values, so the search space is scaled down by that factor before anything else runs;
/// second, the scaled search space is capped at <see cref="MaxScaledSearchSpan"/> entries, and a
/// request that would exceed it returns an explicit <see cref="CloudTenderOutcomeKind.PriceExceedsSearchBound"/>
/// result rather than doing unbounded work. Within that cap, per-row reachability is computed with
/// the standard bounded-knapsack binary-decomposition technique (O(scaledBound * log(quantity)) per
/// row) instead of iterating every individual unit of a large stack quantity one at a time.
/// </summary>
public static class CloudExactTenderEngine
{
    /// <summary>
    /// The largest scaled (GCD-reduced) price this engine will search. Chosen to keep the bounded
    /// dynamic-programming table comfortably small (a few hundred KB per currency row) while
    /// covering every realistic Marketplace price once its denominations are reduced by their GCD.
    /// </summary>
    public const long MaxScaledSearchSpan = 200_000;

    /// <summary>
    /// Selects the deterministic, priority-preferred exact tender for <paramref name="priceUnits"/>
    /// from <paramref name="mix"/>, or reports why none exists.
    /// </summary>
    public static CloudTenderResult TrySelectExactTender(CloudAuthorizedPaymentMix mix, long priceUnits)
    {
        ArgumentNullException.ThrowIfNull(mix);

        if (priceUnits <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(priceUnits), "A tender price must be positive.");
        }

        var rows = mix.SpendingOrder;
        var gcd = GcdOfUnitValues(rows);

        if (priceUnits % gcd != 0)
        {
            return CloudTenderResult.NoExactTenderExists();
        }

        if (TotalAvailableValue(rows) < priceUnits)
        {
            return CloudTenderResult.NoExactTenderExists();
        }

        var scaledPrice = priceUnits / gcd;
        if (scaledPrice > MaxScaledSearchSpan)
        {
            return CloudTenderResult.PriceExceedsSearchBound();
        }

        // suffix[i] = amounts (scaled) reachable using rows[i..n-1]; suffix[n] = {0} only.
        var n = rows.Count;
        var suffix = new bool[n + 1][];
        suffix[n] = new bool[scaledPrice + 1];
        suffix[n][0] = true;

        for (var i = n - 1; i >= 0; i--)
        {
            suffix[i] = FoldRow(suffix[i + 1], rows[i], gcd, scaledPrice);
        }

        if (!suffix[0][scaledPrice])
        {
            return CloudTenderResult.NoExactTenderExists();
        }

        var lines = new List<CloudTenderLine>();
        var remaining = scaledPrice;

        for (var i = 0; i < n; i++)
        {
            var row = rows[i];
            var scaledUnitValue = row.UnitValue / gcd;
            var tail = suffix[i + 1];

            var chosenCount = -1L;
            for (var r = 0L; r <= remaining; r++)
            {
                if (!tail[r])
                {
                    continue;
                }

                var diff = remaining - r;
                if (diff % scaledUnitValue != 0)
                {
                    continue;
                }

                var candidateCount = diff / scaledUnitValue;
                if (candidateCount > row.TotalAvailableQuantity)
                {
                    continue;
                }

                chosenCount = candidateCount;
                break;
            }

            // suffix[i][remaining] was proven true above/at the prior iteration, so a valid count
            // always exists here by construction of FoldRow.
            if (chosenCount > 0)
            {
                lines.AddRange(ConsumeRow(row, chosenCount));
            }

            remaining -= chosenCount * scaledUnitValue;
        }

        return CloudTenderResult.Composed(priceUnits, lines);
    }

    /// <summary>
    /// Finds the smallest exactly payable price at or above <paramref name="floorInclusive"/> and,
    /// when given, at or below <paramref name="ceilingInclusive"/> (MKT-103's Proxy Increment,
    /// MKT-108's Buy It Now overpayment search).
    /// </summary>
    public static CloudPriceSearchResult FindSmallestExactlyPayableAtOrAbove(
        CloudAuthorizedPaymentMix mix, long floorInclusive, long? ceilingInclusive)
    {
        ArgumentNullException.ThrowIfNull(mix);

        if (floorInclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(floorInclusive), "A price search floor must be positive.");
        }

        if (ceilingInclusive is not null && ceilingInclusive.Value < floorInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(ceilingInclusive), "A price search ceiling cannot be below its floor.");
        }

        var rows = mix.SpendingOrder;
        var gcd = GcdOfUnitValues(rows);
        var total = TotalAvailableValue(rows);
        var effectiveCeiling = ceilingInclusive is null ? total : Math.Min(ceilingInclusive.Value, total);

        if (effectiveCeiling < floorInclusive)
        {
            return CloudPriceSearchResult.NotFound();
        }

        var scaledCeiling = effectiveCeiling / gcd;
        if (scaledCeiling > MaxScaledSearchSpan)
        {
            return CloudPriceSearchResult.SearchBoundExceeded();
        }

        var reachable = new bool[scaledCeiling + 1];
        reachable[0] = true;
        foreach (var row in rows)
        {
            reachable = FoldRow(reachable, row, gcd, scaledCeiling);
        }

        var scaledFloor = (floorInclusive + gcd - 1) / gcd;
        for (var scaledAmount = scaledFloor; scaledAmount <= scaledCeiling; scaledAmount++)
        {
            if (reachable[scaledAmount])
            {
                return CloudPriceSearchResult.Found(scaledAmount * gcd);
            }
        }

        return CloudPriceSearchResult.NotFound();
    }

    /// <summary>
    /// The portion of <paramref name="mix"/> a composed <paramref name="tender"/> did not spend
    /// (MKT-106: "release all unused authorized escrow").
    /// </summary>
    public static IReadOnlyList<CloudCurrencyAsset> ComputeUnusedPortion(CloudAuthorizedPaymentMix mix, CloudTenderResult tender)
    {
        ArgumentNullException.ThrowIfNull(mix);
        ArgumentNullException.ThrowIfNull(tender);

        if (!tender.IsComposed)
        {
            throw new ArgumentException("Only a composed tender has an unused portion to release.", nameof(tender));
        }

        var spentByTarget = tender.Lines.ToDictionary(l => l.Target, l => l.QuantitySpent);

        var unused = new List<CloudCurrencyAsset>();
        foreach (var row in mix.SpendingOrder)
        {
            foreach (var asset in row.Assets)
            {
                var spent = spentByTarget.GetValueOrDefault(asset.Target, 0);
                var remaining = asset.AvailableQuantity - spent;
                if (remaining > 0)
                {
                    unused.Add(new CloudCurrencyAsset(asset.Target, remaining));
                }
            }
        }

        return unused;
    }

    private static IEnumerable<CloudTenderLine> ConsumeRow(CloudCurrencyPaymentRow row, long quantityNeeded)
    {
        var orderedTargets = CloudReservationTargetOrdering.Order(row.Assets.Select(a => a.Target));
        var availableByTarget = row.Assets.ToDictionary(a => a.Target, a => a.AvailableQuantity);

        var remainingNeed = quantityNeeded;
        foreach (var target in orderedTargets)
        {
            if (remainingNeed <= 0)
            {
                yield break;
            }

            var take = Math.Min(availableByTarget[target], remainingNeed);
            if (take <= 0)
            {
                continue;
            }

            yield return new CloudTenderLine(row.Wcid, target, row.UnitValue, take);
            remainingNeed -= take;
        }
    }

    /// <summary>
    /// Folds one currency row's contribution into an existing (scaled) reachability array using
    /// bounded-knapsack binary decomposition, so a large stack quantity costs O(log quantity) passes
    /// rather than one pass per individual unit.
    /// </summary>
    private static bool[] FoldRow(bool[] tailReachable, CloudCurrencyPaymentRow row, long gcd, long scaledBound)
    {
        var scaledUnitValue = row.UnitValue / gcd;
        var reachable = (bool[])tailReachable.Clone();

        var remainingAvailable = row.TotalAvailableQuantity;
        var chunkSize = 1L;

        while (remainingAvailable > 0)
        {
            var chunk = Math.Min(chunkSize, remainingAvailable);
            var chunkValue = chunk * scaledUnitValue;

            if (chunkValue <= scaledBound)
            {
                for (var amount = scaledBound; amount >= chunkValue; amount--)
                {
                    if (!reachable[amount] && reachable[amount - chunkValue])
                    {
                        reachable[amount] = true;
                    }
                }
            }

            remainingAvailable -= chunk;
            chunkSize *= 2;
        }

        return reachable;
    }

    private static long GcdOfUnitValues(IReadOnlyList<CloudCurrencyPaymentRow> rows)
    {
        var result = 0L;
        foreach (var row in rows)
        {
            result = Gcd(result, row.UnitValue);
        }

        return result;
    }

    private static long Gcd(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    private static long TotalAvailableValue(IReadOnlyList<CloudCurrencyPaymentRow> rows)
    {
        var total = 0L;
        foreach (var row in rows)
        {
            try
            {
                total = checked(total + row.TotalContributableUnits);
            }
            catch (OverflowException)
            {
                return long.MaxValue;
            }
        }

        return total;
    }
}
