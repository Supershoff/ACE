namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudOwnershipTransferPolicy.Transfer"/>.
/// </summary>
public sealed record CloudOwnershipTransferResult
{
    public bool IsSuccess { get; }

    public CloudAccountId? NewOwnerId { get; }

    public CloudAggregateVersion? NewVersion { get; }

    public CloudCustodyTransitionErrorKind? ErrorKind { get; }

    public string? Reason { get; }

    private CloudOwnershipTransferResult(
        bool isSuccess, CloudAccountId? newOwnerId, CloudAggregateVersion? newVersion, CloudCustodyTransitionErrorKind? errorKind, string? reason)
    {
        IsSuccess = isSuccess;
        NewOwnerId = newOwnerId;
        NewVersion = newVersion;
        ErrorKind = errorKind;
        Reason = reason;
    }

    public static CloudOwnershipTransferResult Success(CloudAccountId newOwnerId, CloudAggregateVersion newVersion)
    {
        ArgumentNullException.ThrowIfNull(newOwnerId);
        ArgumentNullException.ThrowIfNull(newVersion);
        return new CloudOwnershipTransferResult(true, newOwnerId, newVersion, null, null);
    }

    public static CloudOwnershipTransferResult Failure(CloudCustodyTransitionErrorKind errorKind, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failed ownership transfer requires a reason.", nameof(reason));
        }

        return new CloudOwnershipTransferResult(false, null, null, errorKind, reason);
    }
}
