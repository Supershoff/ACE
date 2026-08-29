using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One locked target within one Withdrawal Token's <see cref="CloudWithdrawalReservation"/> aggregate
/// (issue #122, WDR-001, WDR-003, WDR-004, WDR-005, WDR-008, INV-002, INV-003): a whole Cloud Item or
/// a Cloud Stack Lot's quantity claim. A single reservation carries one or more of these rows, all
/// opened or released together with their parent, which is what makes a mixed multi-item/quantity
/// selection reserve or redeem as one atomic unit -- unlike the two independent per-target-type
/// tables this issue replaces, a Withdrawal Token's global uniqueness now lives on exactly one
/// column (<see cref="CloudWithdrawalReservation.TokenHash"/>) shared by every target kind.
/// </summary>
public sealed class CloudWithdrawalReservationTarget
{
    private CloudWithdrawalReservationTarget()
    {
    }

    private CloudWithdrawalReservationTarget(
        Guid id, Guid reservationId, CloudWithdrawalReservationTargetKind kind, uint? itemBiotaId, Guid? stackLotId, int? quantity)
    {
        Id = id;
        ReservationId = reservationId;
        Kind = kind;
        ItemBiotaId = itemBiotaId;
        StackLotId = stackLotId;
        Quantity = quantity;
    }

    public static CloudWithdrawalReservationTarget ForItem(Guid reservationId, uint biotaId)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("A Withdrawal Reservation target requires its parent reservation ID.", nameof(reservationId));
        }

        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "A whole-item Withdrawal Reservation target requires a real native biota GUID.");
        }

        return new CloudWithdrawalReservationTarget(
            Guid.NewGuid(), reservationId, CloudWithdrawalReservationTargetKind.Item, biotaId, stackLotId: null, quantity: null);
    }

    public static CloudWithdrawalReservationTarget ForStackLot(Guid reservationId, Guid lotId, int quantity)
    {
        if (reservationId == Guid.Empty)
        {
            throw new ArgumentException("A Withdrawal Reservation target requires its parent reservation ID.", nameof(reservationId));
        }

        if (lotId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Stack Lot Withdrawal Reservation target requires a real Cloud Stack Lot ID.", nameof(lotId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "A Cloud Stack Lot Withdrawal Reservation target requires a positive quantity.");
        }

        return new CloudWithdrawalReservationTarget(
            Guid.NewGuid(), reservationId, CloudWithdrawalReservationTargetKind.StackLot, itemBiotaId: null, lotId, quantity);
    }

    public Guid Id { get; private set; }

    public Guid ReservationId { get; private set; }

    public CloudWithdrawalReservationTargetKind Kind { get; private set; }

    /// <summary>Set only when <see cref="Kind"/> is <see cref="CloudWithdrawalReservationTargetKind.Item"/>.</summary>
    public uint? ItemBiotaId { get; private set; }

    /// <summary>Set only when <see cref="Kind"/> is <see cref="CloudWithdrawalReservationTargetKind.StackLot"/>.</summary>
    public Guid? StackLotId { get; private set; }

    /// <summary>
    /// The exact quantity reserved from <see cref="StackLotId"/> (INV-002); null for an
    /// <see cref="CloudWithdrawalReservationTargetKind.Item"/> target.
    /// </summary>
    public int? Quantity { get; private set; }

    /// <summary>
    /// Projects this persisted row back onto the pure domain target shape that
    /// <see cref="CloudReservationPolicy"/> reasons about, so exclusivity/lock-ordering decisions
    /// reuse that shared policy instead of duplicating it here (this issue's Refactor section).
    /// </summary>
    public CloudReservationTarget ToPolicyTarget() => Kind switch
    {
        CloudWithdrawalReservationTargetKind.Item => CloudReservationTarget.ForItem(new CloudItemId(ItemBiotaId!.Value)),
        CloudWithdrawalReservationTargetKind.StackLot => CloudReservationTarget.ForStackLot(new CloudStackLotId(StackLotId!.Value)),
        _ => throw new InvalidOperationException("Unrecognized Cloud Withdrawal Reservation target kind."),
    };
}
