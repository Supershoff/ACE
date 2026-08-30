namespace ACE.Cloud.Backend;

/// <summary>
/// One requested Withdrawal Reservation target (WDR-001, INV-002). <see cref="Kind"/> is
/// <c>"Item"</c> (requires <see cref="ItemId"/>) or <c>"StackLot"</c> (requires
/// <see cref="StackLotId"/>). A stack lot target with no <see cref="Quantity"/> reserves the lot's
/// full current quantity (INV-002's "multi-select defaults to all selected quantities"); one with a
/// <see cref="Quantity"/> less than the lot's current amount first splits off exactly that much
/// (requiring <see cref="ExpectedVersion"/>, the lot's optimistic version from the last inventory
/// read the client has) into a new lot and reserves that instead (INV-002's "partial lots").
/// </summary>
public sealed record WithdrawalReservationTargetRequest(string Kind, uint? ItemId, Guid? StackLotId, int? Quantity, int? ExpectedVersion);

public sealed record OpenWithdrawalReservationRequest(IReadOnlyList<WithdrawalReservationTargetRequest> Targets);

public sealed record CancelWithdrawalReservationRequest(int ExpectedVersion);
