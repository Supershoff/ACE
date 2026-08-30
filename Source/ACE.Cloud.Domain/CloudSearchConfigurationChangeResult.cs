namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of one <see cref="CloudSearchConfigurationPolicy.SetRegexSearchEnabled"/> transition:
/// either the new configuration, or a rejection reason an administrator command can display directly.
/// </summary>
public sealed record CloudSearchConfigurationChangeResult
{
    public bool IsSuccess { get; }

    public CloudSearchConfiguration? Configuration { get; }

    public string? Reason { get; }

    private CloudSearchConfigurationChangeResult(bool isSuccess, CloudSearchConfiguration? configuration, string? reason)
    {
        IsSuccess = isSuccess;
        Configuration = configuration;
        Reason = reason;
    }

    public static CloudSearchConfigurationChangeResult Success(CloudSearchConfiguration configuration) =>
        new(true, configuration ?? throw new ArgumentNullException(nameof(configuration)), null);

    public static CloudSearchConfigurationChangeResult Failure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected Safe Regex Search configuration change requires a reason.", nameof(reason));
        }

        return new CloudSearchConfigurationChangeResult(false, null, reason);
    }
}
