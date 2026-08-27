namespace ACE.Cloud.Domain;

/// <summary>
/// A bidder's complete Authorized Payment Mix (MKT-101, MKT-102): the exact currency items and
/// stack quantities the tender/proxy engines may ever spend, in the bidder's own drag-to-order
/// spending priority. Constructing an instance is the single place duplicate-asset and
/// duplicate-currency input is rejected, so every downstream engine can assume a clean, exclusive
/// mix (issue #9 Red section: "duplicate authorized assets" and "one asset backing two
/// obligations" -- within one mix; the same asset backing two different bidders' obligations
/// remains <see cref="CloudReservationPolicy"/>'s already-covered exclusivity concern and is not
/// duplicated here).
/// </summary>
public sealed class CloudAuthorizedPaymentMix
{
    /// <summary>Rows ordered by <see cref="CloudCurrencyPaymentRow.PriorityRank"/>; index 0 spends first.</summary>
    public IReadOnlyList<CloudCurrencyPaymentRow> SpendingOrder { get; }

    public CloudAuthorizedPaymentMix(IReadOnlyList<CloudCurrencyPaymentRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            throw new ArgumentException("An authorized payment mix requires at least one currency row.", nameof(rows));
        }

        if (rows.Select(r => r.Wcid).Distinct().Count() != rows.Count)
        {
            throw new ArgumentException("An authorized payment mix cannot list the same currency WCID in two rows.", nameof(rows));
        }

        if (rows.Select(r => r.PriorityRank).Distinct().Count() != rows.Count)
        {
            throw new ArgumentException("An authorized payment mix requires a unique spending-priority rank per row.", nameof(rows));
        }

        var allTargets = rows.SelectMany(r => r.Assets.Select(a => a.Target)).ToList();
        if (allTargets.Distinct().Count() != allTargets.Count)
        {
            throw new ArgumentException(
                "The same authorized currency asset cannot back more than one row of the same payment mix.", nameof(rows));
        }

        SpendingOrder = rows.OrderBy(r => r.PriorityRank).ToList();
    }
}
