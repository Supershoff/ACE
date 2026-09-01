namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of one <see cref="CloudSharingGrantPolicy.EvaluateSet"/> attempt: either the resolved
/// grantee account plus the requested level (the caller still performs the actual upsert against
/// whatever grant currently exists for that owner/grantee pair), or a refusal with one exact
/// <see cref="CloudSharingGrantRejectionCode"/> (mirrors <see cref="CloudTransferOfferCreateResult"/>'s
/// established shape).
/// </summary>
public sealed record CloudSharingGrantSetResult
{
    public bool IsSuccess { get; }

    public CloudAccountId? GranteeAccountId { get; }

    public CloudSharingGrantLevel? Level { get; }

    public CloudSharingGrantRejectionCode RejectionCode { get; }

    public string? Reason { get; }

    private CloudSharingGrantSetResult(
        bool isSuccess, CloudAccountId? granteeAccountId, CloudSharingGrantLevel? level, CloudSharingGrantRejectionCode rejectionCode, string? reason)
    {
        IsSuccess = isSuccess;
        GranteeAccountId = granteeAccountId;
        Level = level;
        RejectionCode = rejectionCode;
        Reason = reason;
    }

    public static CloudSharingGrantSetResult Success(CloudAccountId granteeAccountId, CloudSharingGrantLevel level) =>
        new(true, granteeAccountId ?? throw new ArgumentNullException(nameof(granteeAccountId)), level, CloudSharingGrantRejectionCode.None, reason: null);

    public static CloudSharingGrantSetResult Failure(CloudSharingGrantRejectionCode rejectionCode, string reason)
    {
        if (rejectionCode == CloudSharingGrantRejectionCode.None)
        {
            throw new ArgumentException("A rejected Sharing Grant result requires a real rejection code.", nameof(rejectionCode));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected Sharing Grant result requires a reason.", nameof(reason));
        }

        return new(false, granteeAccountId: null, level: null, rejectionCode, reason);
    }
}
