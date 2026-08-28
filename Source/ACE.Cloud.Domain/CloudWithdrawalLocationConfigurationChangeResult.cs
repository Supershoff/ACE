namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of one <see cref="CloudWithdrawalLocationConfigurationPolicy"/> transition: either
/// the new configuration, or a rejection reason an administrator command can display directly.
/// </summary>
public sealed record CloudWithdrawalLocationConfigurationChangeResult
{
    public bool IsSuccess { get; }

    public CloudWithdrawalLocationConfiguration? Configuration { get; }

    public string? Reason { get; }

    private CloudWithdrawalLocationConfigurationChangeResult(bool isSuccess, CloudWithdrawalLocationConfiguration? configuration, string? reason)
    {
        IsSuccess = isSuccess;
        Configuration = configuration;
        Reason = reason;
    }

    public static CloudWithdrawalLocationConfigurationChangeResult Success(CloudWithdrawalLocationConfiguration configuration) =>
        new(true, configuration ?? throw new ArgumentNullException(nameof(configuration)), null);

    public static CloudWithdrawalLocationConfigurationChangeResult Failure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected configuration change requires a reason.", nameof(reason));
        }

        return new CloudWithdrawalLocationConfigurationChangeResult(false, null, reason);
    }
}
