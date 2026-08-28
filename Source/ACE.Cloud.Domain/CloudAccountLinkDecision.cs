namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudAccountLinkPolicy.EvaluateLink"/> or
/// <see cref="CloudAccountLinkPolicy.EvaluateUnlink"/>: either approved, or refused with one exact
/// <see cref="CloudAccountLinkRejectionCode"/>.
/// </summary>
public sealed record CloudAccountLinkDecision
{
    public bool IsApproved { get; }

    public CloudAccountLinkRejectionCode RejectionCode { get; }

    private CloudAccountLinkDecision(bool isApproved, CloudAccountLinkRejectionCode rejectionCode)
    {
        IsApproved = isApproved;
        RejectionCode = rejectionCode;
    }

    public static CloudAccountLinkDecision Approved() => new(true, CloudAccountLinkRejectionCode.None);

    public static CloudAccountLinkDecision Rejected(CloudAccountLinkRejectionCode rejectionCode)
    {
        if (rejectionCode == CloudAccountLinkRejectionCode.None)
        {
            throw new ArgumentException("A rejected account link decision requires a real rejection code.", nameof(rejectionCode));
        }

        return new CloudAccountLinkDecision(false, rejectionCode);
    }
}
