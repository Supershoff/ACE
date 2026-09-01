namespace ACE.Cloud.Persistence;

/// <summary>
/// One target a caller asks <see cref="CloudTransferOfferGateway.CreateAsync"/> to lock under one new
/// Transfer Offer (issue #35, XFER-001, XFER-002): a whole Cloud Item by its native biota GUID, or an
/// entire Cloud Stack Lot by its ID. Mirrors <see cref="CloudWithdrawalReservationRequestTarget"/>'s
/// established shape (reusing its <see cref="CloudWithdrawalReservationTargetKind"/> marker rather
/// than a second copy of the same two-value distinction) and its same "always the lot's full current
/// quantity" contract (INV-002).
/// </summary>
public sealed record CloudTransferOfferRequestTarget
{
    public CloudWithdrawalReservationTargetKind Kind { get; }

    public uint ItemBiotaId { get; }

    public Guid StackLotId { get; }

    private CloudTransferOfferRequestTarget(CloudWithdrawalReservationTargetKind kind, uint itemBiotaId, Guid stackLotId)
    {
        Kind = kind;
        ItemBiotaId = itemBiotaId;
        StackLotId = stackLotId;
    }

    public static CloudTransferOfferRequestTarget ForItem(uint biotaId)
    {
        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "A whole-item Transfer Offer target requires a real native biota GUID.");
        }

        return new CloudTransferOfferRequestTarget(CloudWithdrawalReservationTargetKind.Item, biotaId, Guid.Empty);
    }

    public static CloudTransferOfferRequestTarget ForStackLot(Guid lotId)
    {
        if (lotId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Stack Lot Transfer Offer target requires a real Cloud Stack Lot ID.", nameof(lotId));
        }

        return new CloudTransferOfferRequestTarget(CloudWithdrawalReservationTargetKind.StackLot, 0, lotId);
    }
}
