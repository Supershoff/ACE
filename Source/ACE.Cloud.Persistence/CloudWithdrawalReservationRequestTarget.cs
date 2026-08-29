using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One target a caller asks <see cref="CloudCustodyBoundary.ReserveForWithdrawalAsync(System.Collections.Generic.IReadOnlyList{CloudWithdrawalReservationRequestTarget}, string, Guid, string, TimeSpan, Guid, System.Threading.CancellationToken)"/>
/// to lock under one Withdrawal Token (issue #122, WDR-001, WDR-003): a whole Cloud Item by its
/// native biota GUID, or an entire Cloud Stack Lot by its ID. A stack lot target always reserves
/// that lot's complete current quantity -- exactly the same granularity
/// <see cref="CloudReservationTarget.ForStackLot"/> already models -- so a caller who wants fewer
/// than a lot's full quantity must first split off a new lot for exactly that amount through
/// <see cref="CloudStackLotTransactionAuthority.SplitLotAsync"/> and reserve that new lot instead.
/// </summary>
public sealed record CloudWithdrawalReservationRequestTarget
{
    public CloudWithdrawalReservationTargetKind Kind { get; }

    public uint ItemBiotaId { get; }

    public Guid StackLotId { get; }

    private CloudWithdrawalReservationRequestTarget(CloudWithdrawalReservationTargetKind kind, uint itemBiotaId, Guid stackLotId)
    {
        Kind = kind;
        ItemBiotaId = itemBiotaId;
        StackLotId = stackLotId;
    }

    public static CloudWithdrawalReservationRequestTarget ForItem(uint biotaId)
    {
        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "A whole-item withdrawal target requires a real native biota GUID.");
        }

        return new CloudWithdrawalReservationRequestTarget(CloudWithdrawalReservationTargetKind.Item, biotaId, Guid.Empty);
    }

    public static CloudWithdrawalReservationRequestTarget ForStackLot(Guid lotId)
    {
        if (lotId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Stack Lot withdrawal target requires a real Cloud Stack Lot ID.", nameof(lotId));
        }

        return new CloudWithdrawalReservationRequestTarget(CloudWithdrawalReservationTargetKind.StackLot, 0, lotId);
    }
}
