namespace ACE.Cloud.Persistence;

/// <summary>
/// One delivered biota from a redeemed multi-target Withdrawal Reservation (issue #122, INV-003):
/// the original biota GUID for a whole-item target or a full-lot delivery, or a materialized child
/// GUID for a partial Cloud Stack Lot delivery. <see cref="Quantity"/> is null for a whole-item
/// delivery and the exact delivered quantity for a stack lot delivery.
/// </summary>
public sealed record CloudWithdrawalDeliveryItem(uint DeliveredBiotaId, int? Quantity);
