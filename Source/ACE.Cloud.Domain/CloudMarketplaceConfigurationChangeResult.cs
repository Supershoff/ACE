namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of one <see cref="CloudMarketplaceStatePolicy.SetState"/> transition: either the new
/// configuration, or a rejection reason an administrator command can display directly.
/// </summary>
public sealed record CloudMarketplaceConfigurationChangeResult
{
    public bool IsSuccess { get; }

    public CloudMarketplaceConfiguration? Configuration { get; }

    public string? Reason { get; }

    private CloudMarketplaceConfigurationChangeResult(bool isSuccess, CloudMarketplaceConfiguration? configuration, string? reason)
    {
        IsSuccess = isSuccess;
        Configuration = configuration;
        Reason = reason;
    }

    public static CloudMarketplaceConfigurationChangeResult Success(CloudMarketplaceConfiguration configuration) =>
        new(true, configuration ?? throw new ArgumentNullException(nameof(configuration)), null);

    public static CloudMarketplaceConfigurationChangeResult Failure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected Marketplace State change requires a reason.", nameof(reason));
        }

        return new CloudMarketplaceConfigurationChangeResult(false, null, reason);
    }
}
