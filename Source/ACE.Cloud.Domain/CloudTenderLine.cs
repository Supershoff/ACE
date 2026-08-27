namespace ACE.Cloud.Domain;

/// <summary>
/// One line of a composed exact tender (MKT-102's tender preview, MKT-106's settlement): the
/// specific authorized asset and exact quantity the exact-tender engine selected, plus the Unit
/// value used to compute its contribution so a caller never has to re-look-up the seller's Currency
/// Terms Snapshot to render or settle the preview.
/// </summary>
public sealed record CloudTenderLine
{
    public int Wcid { get; }

    public CloudReservationTarget Target { get; }

    public long UnitValue { get; }

    public long QuantitySpent { get; }

    public CloudTenderLine(int wcid, CloudReservationTarget target, long unitValue, long quantitySpent)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (unitValue <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitValue), "A tender line requires a positive Unit value.");
        }

        if (quantitySpent <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantitySpent), "A tender line requires a positive quantity spent.");
        }

        Wcid = wcid;
        Target = target;
        UnitValue = unitValue;
        QuantitySpent = quantitySpent;
    }

    public long UnitsContributed => UnitValue * QuantitySpent;
}
