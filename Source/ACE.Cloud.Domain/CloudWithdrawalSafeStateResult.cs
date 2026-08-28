namespace ACE.Cloud.Domain;

/// <summary>The outcome of one <see cref="CloudWithdrawalSafeStatePolicy.Evaluate"/> check.</summary>
public sealed record CloudWithdrawalSafeStateResult
{
    public bool IsSafe { get; }

    public CloudWithdrawalSafeStateRejectionCode? RejectionCode { get; }

    public string? Reason { get; }

    private CloudWithdrawalSafeStateResult(bool isSafe, CloudWithdrawalSafeStateRejectionCode? rejectionCode, string? reason)
    {
        IsSafe = isSafe;
        RejectionCode = rejectionCode;
        Reason = reason;
    }

    public static CloudWithdrawalSafeStateResult Safe() => new(true, null, null);

    public static CloudWithdrawalSafeStateResult Unsafe(CloudWithdrawalSafeStateRejectionCode code, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected safe-state check requires a reason.", nameof(reason));
        }

        return new CloudWithdrawalSafeStateResult(false, code, reason);
    }
}
