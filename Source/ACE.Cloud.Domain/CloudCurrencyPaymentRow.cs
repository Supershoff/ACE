namespace ACE.Cloud.Domain;

/// <summary>
/// One drag-ordered currency row of a bidder's Authorized Payment Mix (MKT-102): every asset in the
/// row shares the same accepted-currency WCID and the seller's Currency Terms Snapshot Unit value
/// for that WCID. <see cref="PriorityRank"/> is the bidder's own drag spending priority (0 spends
/// first); it is independent of <see cref="Wcid"/> ordering so callers may freely reorder rows
/// without renaming currencies. <see cref="TotalAvailableQuantity"/> and
/// <see cref="TotalContributableUnits"/> are validated once here (issue #9 Red section: "overflow
/// bounds") so every downstream engine can add them without re-checking for overflow.
/// </summary>
public sealed record CloudCurrencyPaymentRow
{
    public int Wcid { get; }

    public long UnitValue { get; }

    public int PriorityRank { get; }

    public IReadOnlyList<CloudCurrencyAsset> Assets { get; }

    /// <summary>Total quantity authorized across every asset in this row.</summary>
    public long TotalAvailableQuantity { get; }

    /// <summary>The total Units this row could ever contribute to a tender (<see cref="UnitValue"/> * <see cref="TotalAvailableQuantity"/>).</summary>
    public long TotalContributableUnits { get; }

    public CloudCurrencyPaymentRow(int wcid, long unitValue, int priorityRank, IReadOnlyList<CloudCurrencyAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (unitValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitValue), "A currency row requires a positive Unit value.");
        }

        if (assets.Count == 0)
        {
            throw new ArgumentException("A currency row requires at least one authorized asset.", nameof(assets));
        }

        if (assets.Select(a => a.Target).Distinct().Count() != assets.Count)
        {
            throw new ArgumentException("A currency row cannot list the same authorized asset more than once.", nameof(assets));
        }

        long totalQuantity;
        long totalUnits;
        try
        {
            totalQuantity = 0;
            foreach (var asset in assets)
            {
                totalQuantity = checked(totalQuantity + asset.AvailableQuantity);
            }

            totalUnits = checked(unitValue * totalQuantity);
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(
                nameof(assets), "A currency row's total authorized quantity or value overflows the supported range.");
        }

        Wcid = wcid;
        UnitValue = unitValue;
        PriorityRank = priorityRank;
        Assets = assets;
        TotalAvailableQuantity = totalQuantity;
        TotalContributableUnits = totalUnits;
    }
}
