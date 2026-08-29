namespace ACE.Cloud.Persistence;

/// <summary>
/// The committed result of redeeming one Withdrawal Token's reservation (issue #122, WDR-001,
/// WDR-003): every native biota delivered into <see cref="RecipientContainerId"/>, in the same order
/// its <see cref="CloudWithdrawalReservationTarget"/> rows were locked. A multi-item/quantity
/// redemption delivers every reserved target or none of them, so this list is always exactly as long
/// as the reservation's target set.
/// </summary>
public sealed record CloudMultiWithdrawalResult(
    IReadOnlyList<CloudWithdrawalDeliveryItem> Deliveries, uint RecipientContainerId, Guid FormerOwnerId);
