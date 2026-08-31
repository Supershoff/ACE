namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudTransferOfferPolicy.Accept"/>, <see cref="CloudTransferOfferPolicy.Cancel"/>,
/// <see cref="CloudTransferOfferPolicy.Decline"/>, or <see cref="CloudTransferOfferPolicy.Expire"/>:
/// either approved, carrying the offer's new resolved state, or refused with one exact
/// <see cref="CloudTransferOfferRejectionCode"/>.
/// </summary>
public sealed record CloudTransferOfferCommandResult
{
    public bool IsSuccess { get; }

    public CloudTransferOffer? Offer { get; }

    public CloudTransferOfferRejectionCode RejectionCode { get; }

    public string? Reason { get; }

    private CloudTransferOfferCommandResult(
        bool isSuccess, CloudTransferOffer? offer, CloudTransferOfferRejectionCode rejectionCode, string? reason)
    {
        IsSuccess = isSuccess;
        Offer = offer;
        RejectionCode = rejectionCode;
        Reason = reason;
    }

    public static CloudTransferOfferCommandResult Success(CloudTransferOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return new CloudTransferOfferCommandResult(true, offer, CloudTransferOfferRejectionCode.None, null);
    }

    public static CloudTransferOfferCommandResult Failure(CloudTransferOfferRejectionCode rejectionCode, string reason)
    {
        if (rejectionCode == CloudTransferOfferRejectionCode.None)
        {
            throw new ArgumentException("A refused Transfer Offer command requires a real rejection code.", nameof(rejectionCode));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A refused Transfer Offer command requires a reason.", nameof(reason));
        }

        return new CloudTransferOfferCommandResult(false, null, rejectionCode, reason);
    }
}
