namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudTransferOfferPolicy.Create"/>: either approved, carrying the new
/// offer plus its backing reservation and allocations, or refused with one exact
/// <see cref="CloudTransferOfferRejectionCode"/>.
/// </summary>
public sealed record CloudTransferOfferCreateResult
{
    public bool IsSuccess { get; }

    public CloudTransferOffer? Offer { get; }

    public CloudReservation? Reservation { get; }

    public IReadOnlyList<CloudReservationAllocation> Allocations { get; }

    public CloudTransferOfferRejectionCode RejectionCode { get; }

    public string? Reason { get; }

    private CloudTransferOfferCreateResult(
        bool isSuccess,
        CloudTransferOffer? offer,
        CloudReservation? reservation,
        IReadOnlyList<CloudReservationAllocation> allocations,
        CloudTransferOfferRejectionCode rejectionCode,
        string? reason)
    {
        IsSuccess = isSuccess;
        Offer = offer;
        Reservation = reservation;
        Allocations = allocations;
        RejectionCode = rejectionCode;
        Reason = reason;
    }

    public static CloudTransferOfferCreateResult Success(
        CloudTransferOffer offer, CloudReservation reservation, IReadOnlyList<CloudReservationAllocation> allocations)
    {
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(reservation);
        ArgumentNullException.ThrowIfNull(allocations);
        return new CloudTransferOfferCreateResult(true, offer, reservation, allocations, CloudTransferOfferRejectionCode.None, null);
    }

    public static CloudTransferOfferCreateResult Failure(CloudTransferOfferRejectionCode rejectionCode, string reason)
    {
        if (rejectionCode == CloudTransferOfferRejectionCode.None)
        {
            throw new ArgumentException("A refused Transfer Offer creation requires a real rejection code.", nameof(rejectionCode));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A refused Transfer Offer creation requires a reason.", nameof(reason));
        }

        return new CloudTransferOfferCreateResult(false, null, null, [], rejectionCode, reason);
    }
}
