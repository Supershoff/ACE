using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// Representative command payload: reserve a Cloud Item for a new Withdrawal Token (WDR-001).
/// Carried inside a <see cref="CloudCommandEnvelope{TCommand}"/>, which supplies the shard,
/// idempotency key, actor identity, and (for this reservation of an existing custody record) the
/// expected aggregate version.
/// </summary>
public sealed record CloudWithdrawalReservationCommand
{
    public CloudItemId ItemId { get; }

    public CloudAccountId OwnerId { get; }

    public CloudReservationId ReservationId { get; }

    public CloudWithdrawalReservationCommand(CloudItemId itemId, CloudAccountId ownerId, CloudReservationId reservationId)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(reservationId);

        ItemId = itemId;
        OwnerId = ownerId;
        ReservationId = reservationId;
    }
}
