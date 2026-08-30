namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of checking one new count-increasing obligation (a deposit, an accepted incoming
/// offer/purchase, or a vault take) against a Storage Quota limit (INV-004, INV-005, INV-006).
/// </summary>
public sealed record CloudStorageQuotaCheckResult
{
    public bool IsSuccess { get; }

    public string? Reason { get; }

    private CloudStorageQuotaCheckResult(bool isSuccess, string? reason)
    {
        IsSuccess = isSuccess;
        Reason = reason;
    }

    public static CloudStorageQuotaCheckResult Success() => new(true, null);

    public static CloudStorageQuotaCheckResult Failure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A refused quota check requires a reason.", nameof(reason));
        }

        return new CloudStorageQuotaCheckResult(false, reason);
    }
}
