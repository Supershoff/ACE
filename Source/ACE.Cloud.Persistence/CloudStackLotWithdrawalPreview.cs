namespace ACE.Cloud.Persistence;

/// <summary>
/// A non-authoritative preview of what redeeming a Cloud Stack Lot Withdrawal Reservation will
/// require: the backing biota's GUID (to load its weenie identity) and whether this lot currently
/// looks like the sole lot on its stack (so the caller knows whether to pre-allocate a materialized
/// child GUID before calling <see cref="CloudCustodyBoundary.RedeemStackLotWithdrawalReservationAsync"/>).
/// Not a lock-held fact: the actual redemption re-derives <see cref="IsSoleLotOnStack"/> itself under
/// its own row lock and refuses the request if a materialized GUID turns out to be required after
/// all (a legitimate, retryable Conflict, not a custody violation).
/// </summary>
public sealed record CloudStackLotWithdrawalPreview(uint BackingBiotaId, bool IsSoleLotOnStack);
