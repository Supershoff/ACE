namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of one <see cref="CloudCustodianConfigurationPolicy"/> transition: either the new
/// configuration, or a rejection reason an administrator command can display directly.
/// </summary>
public sealed record CloudCustodianConfigurationChangeResult
{
    public bool IsSuccess { get; }

    public CloudCustodianConfiguration? Configuration { get; }

    public string? Reason { get; }

    private CloudCustodianConfigurationChangeResult(bool isSuccess, CloudCustodianConfiguration? configuration, string? reason)
    {
        IsSuccess = isSuccess;
        Configuration = configuration;
        Reason = reason;
    }

    public static CloudCustodianConfigurationChangeResult Success(CloudCustodianConfiguration configuration) =>
        new(true, configuration ?? throw new ArgumentNullException(nameof(configuration)), null);

    public static CloudCustodianConfigurationChangeResult Failure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected configuration change requires a reason.", nameof(reason));
        }

        return new CloudCustodianConfigurationChangeResult(false, null, reason);
    }
}
