namespace ACE.Cloud.Domain;

/// <summary>
/// One specific authorized currency source within a bidder's Authorized Payment Mix (MKT-101): a
/// single non-stackable currency item, or a bidder-authorized quantity claim against one Cloud
/// Stack Lot. Never a raw WCID or an abstract balance -- only an actual reservable
/// <see cref="CloudReservationTarget"/> the tender engine may spend from (IMPLEMENTATION-BRIEF.md:
/// "every bid is collateralized by actual accepted-currency Cloud Items or stack quantities").
/// </summary>
public sealed record CloudCurrencyAsset
{
    public CloudReservationTarget Target { get; }

    /// <summary>
    /// How many whole Unit-valued quantities of this asset the bidder has authorized for spending.
    /// Always exactly 1 for a non-stackable <see cref="CloudReservationTargetKind.Item"/>; may be
    /// any positive quantity for a <see cref="CloudReservationTargetKind.StackLot"/> (ARCH-010).
    /// </summary>
    public long AvailableQuantity { get; }

    public CloudCurrencyAsset(CloudReservationTarget target, long availableQuantity)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (availableQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableQuantity), "An authorized currency asset requires a positive available quantity.");
        }

        if (target.Kind == CloudReservationTargetKind.Item && availableQuantity != 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(availableQuantity), "A non-stackable currency item always contributes exactly one whole unit.");
        }

        Target = target;
        AvailableQuantity = availableQuantity;
    }
}
