using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One locked target within one <see cref="CloudTransferOfferRecord"/> (issue #35, XFER-001,
/// XFER-002, INV-002): a whole Cloud Item or a Cloud Stack Lot's quantity claim. Mirrors
/// <see cref="CloudWithdrawalReservationTarget"/>'s established shape exactly, reusing the already
/// generic <see cref="CloudReservationTargetKind"/> rather than a second withdrawal-flavored copy of
/// the same two-value enum. A Cloud Stack Lot target always reserves that lot's entire current
/// quantity at offer-creation time -- a caller offering fewer than a lot's full quantity must first
/// split off a new lot for exactly that amount through <see cref="CloudStackLotTransactionAuthority.SplitLotAsync"/>
/// and offer that new lot instead (the same contract <see cref="CloudCustodyBoundary.ReserveForWithdrawalAsync(System.Collections.Generic.IReadOnlyList{CloudWithdrawalReservationRequestTarget}, string, Guid, string, TimeSpan, Guid, System.Threading.CancellationToken)"/>
/// already establishes for Withdrawal Reservations).
/// </summary>
public sealed class CloudTransferOfferTargetRecord
{
    private CloudTransferOfferTargetRecord()
    {
    }

    private CloudTransferOfferTargetRecord(
        Guid id, Guid offerId, CloudReservationTargetKind kind, uint? itemBiotaId, Guid? stackLotId, int? quantity)
    {
        Id = id;
        OfferId = offerId;
        Kind = kind;
        ItemBiotaId = itemBiotaId;
        StackLotId = stackLotId;
        Quantity = quantity;
    }

    public static CloudTransferOfferTargetRecord ForItem(Guid offerId, uint biotaId)
    {
        if (offerId == Guid.Empty)
        {
            throw new ArgumentException("A Transfer Offer target requires its parent offer ID.", nameof(offerId));
        }

        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "A whole-item Transfer Offer target requires a real native biota GUID.");
        }

        return new CloudTransferOfferTargetRecord(Guid.NewGuid(), offerId, CloudReservationTargetKind.Item, biotaId, stackLotId: null, quantity: null);
    }

    public static CloudTransferOfferTargetRecord ForStackLot(Guid offerId, Guid lotId, int quantity)
    {
        if (offerId == Guid.Empty)
        {
            throw new ArgumentException("A Transfer Offer target requires its parent offer ID.", nameof(offerId));
        }

        if (lotId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Stack Lot Transfer Offer target requires a real Cloud Stack Lot ID.", nameof(lotId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A Cloud Stack Lot Transfer Offer target requires a positive quantity.");
        }

        return new CloudTransferOfferTargetRecord(Guid.NewGuid(), offerId, CloudReservationTargetKind.StackLot, itemBiotaId: null, lotId, quantity);
    }

    public Guid Id { get; private set; }

    public Guid OfferId { get; private set; }

    public CloudReservationTargetKind Kind { get; private set; }

    /// <summary>Set only when <see cref="Kind"/> is <see cref="CloudReservationTargetKind.Item"/>.</summary>
    public uint? ItemBiotaId { get; private set; }

    /// <summary>Set only when <see cref="Kind"/> is <see cref="CloudReservationTargetKind.StackLot"/>.</summary>
    public Guid? StackLotId { get; private set; }

    /// <summary>The exact quantity offered from <see cref="StackLotId"/> (INV-002); null for an Item target.</summary>
    public int? Quantity { get; private set; }

    /// <summary>Projects this row onto the pure domain target shape <see cref="CloudReservationPolicy"/> reasons about.</summary>
    public CloudReservationTarget ToPolicyTarget() => Kind switch
    {
        CloudReservationTargetKind.Item => CloudReservationTarget.ForItem(new CloudItemId(ItemBiotaId!.Value)),
        CloudReservationTargetKind.StackLot => CloudReservationTarget.ForStackLot(new CloudStackLotId(StackLotId!.Value)),
        _ => throw new InvalidOperationException("Unrecognized Cloud Transfer Offer target kind."),
    };
}
