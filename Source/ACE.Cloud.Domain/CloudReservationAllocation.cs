namespace ACE.Cloud.Domain;

/// <summary>
/// One row projecting a <see cref="CloudReservation"/>'s exclusive claim onto exactly one
/// <see cref="CloudReservationTarget"/> (the "allocation rows" this issue's Green section lists
/// alongside typed exclusive reservations). A multi-target reservation -- for example a multi-item
/// Withdrawal Token or a multi-item Transfer Offer -- is backed by one allocation row per target, and
/// <see cref="CloudReservationPolicy.Open"/> produces every row for a request or none of them (the
/// "All-or-none multi-asset transitions are expressible without partial aggregate commits"
/// acceptance criterion).
/// </summary>
public sealed record CloudReservationAllocation
{
    public CloudReservationId ReservationId { get; }

    public CloudReservationTarget Target { get; }

    public CloudReservationKind Kind { get; }

    public CloudReservationStatus Status { get; }

    public CloudReservationAllocation(
        CloudReservationId reservationId, CloudReservationTarget target, CloudReservationKind kind, CloudReservationStatus status)
    {
        ArgumentNullException.ThrowIfNull(reservationId);
        ArgumentNullException.ThrowIfNull(target);

        ReservationId = reservationId;
        Target = target;
        Kind = kind;
        Status = status;
    }
}
