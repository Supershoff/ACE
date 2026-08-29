namespace ACE.Cloud.Persistence;

/// <summary>
/// One delivered biota from a committed multi-target Withdrawal Reservation redemption, persisted
/// under the redemption's own <see cref="CloudIdempotencyRecord"/> (issue #122, ARCH-006, transaction
/// rule 4) so a repeated request with the same idempotency key can replay the complete original
/// <see cref="CloudMultiWithdrawalResult"/> -- not just the single biota/quantity pair
/// <see cref="CloudIdempotencyRecord"/> itself can represent -- instead of only its first target.
/// Mirrors <see cref="CloudPyrealConversionMmd"/>'s child-rows-under-one-idempotency-key shape.
/// </summary>
public sealed class CloudWithdrawalRedemptionDeliveryItem
{
    private CloudWithdrawalRedemptionDeliveryItem()
    {
    }

    public CloudWithdrawalRedemptionDeliveryItem(Guid redemptionIdempotencyKey, int ordinalPosition, uint deliveredBiotaId, int? quantity)
    {
        if (redemptionIdempotencyKey == Guid.Empty)
        {
            throw new ArgumentException("A redemption delivery row requires its owning redemption's idempotency key.", nameof(redemptionIdempotencyKey));
        }

        if (ordinalPosition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinalPosition), "A redemption delivery row requires a non-negative ordinal position.");
        }

        if (deliveredBiotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deliveredBiotaId), "A redemption delivery row requires a real native biota GUID.");
        }

        if (quantity is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A redemption delivery row's quantity, when present, must be positive.");
        }

        Id = Guid.NewGuid();
        RedemptionIdempotencyKey = redemptionIdempotencyKey;
        OrdinalPosition = ordinalPosition;
        DeliveredBiotaId = deliveredBiotaId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }

    public Guid RedemptionIdempotencyKey { get; private set; }

    /// <summary>Preserves the original delivery order (the same order targets were locked in) across replay.</summary>
    public int OrdinalPosition { get; private set; }

    public uint DeliveredBiotaId { get; private set; }

    /// <summary>Null for a whole-item delivery; the exact delivered quantity for a stack lot delivery.</summary>
    public int? Quantity { get; private set; }
}
