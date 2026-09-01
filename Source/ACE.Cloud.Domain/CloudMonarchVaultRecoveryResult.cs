namespace ACE.Cloud.Domain;

/// <summary>The outcome of <see cref="CloudMonarchVaultRecoveryPolicy.Authorize"/>.</summary>
public sealed record CloudMonarchVaultRecoveryResult
{
    public bool IsSuccess { get; }

    public CloudMonarchVaultRecoveryRejectionCode? RejectionCode { get; }

    public string? Reason { get; }

    private CloudMonarchVaultRecoveryResult(bool isSuccess, CloudMonarchVaultRecoveryRejectionCode? rejectionCode, string? reason)
    {
        IsSuccess = isSuccess;
        RejectionCode = rejectionCode;
        Reason = reason;
    }

    public static CloudMonarchVaultRecoveryResult Success() => new(true, null, null);

    public static CloudMonarchVaultRecoveryResult Failure(CloudMonarchVaultRecoveryRejectionCode rejectionCode, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A failed Allegiance Vault recovery requires a reason.", nameof(reason));
        }

        return new CloudMonarchVaultRecoveryResult(false, rejectionCode, reason);
    }
}
