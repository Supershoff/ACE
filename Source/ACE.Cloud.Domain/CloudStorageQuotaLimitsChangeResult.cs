namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of one <see cref="CloudStorageQuotaPolicy"/> limit change: either the new limits, or a
/// rejection reason an administrator command can display directly.
/// </summary>
public sealed record CloudStorageQuotaLimitsChangeResult
{
    public bool IsSuccess { get; }

    public CloudStorageQuotaLimits? Limits { get; }

    public string? Reason { get; }

    private CloudStorageQuotaLimitsChangeResult(bool isSuccess, CloudStorageQuotaLimits? limits, string? reason)
    {
        IsSuccess = isSuccess;
        Limits = limits;
        Reason = reason;
    }

    public static CloudStorageQuotaLimitsChangeResult Success(CloudStorageQuotaLimits limits) =>
        new(true, limits ?? throw new ArgumentNullException(nameof(limits)), null);

    public static CloudStorageQuotaLimitsChangeResult Failure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected Storage Quota limit change requires a reason.", nameof(reason));
        }

        return new CloudStorageQuotaLimitsChangeResult(false, null, reason);
    }
}
