namespace ACE.Cloud.Hosting;

/// <summary>
/// The outcome of probing one <see cref="CloudStartupComponent"/>. Never thrown or inferred from a
/// timeout (transaction rule 8) -- always an explicit, authoritative read result.
/// </summary>
public sealed record CloudStartupCheckResult
{
    private CloudStartupCheckResult(CloudStartupComponent component, bool isHealthy, string? reason)
    {
        Component = component;
        IsHealthy = isHealthy;
        Reason = reason;
    }

    public CloudStartupComponent Component { get; }

    public bool IsHealthy { get; }

    public string? Reason { get; }

    public static CloudStartupCheckResult Healthy(CloudStartupComponent component) => new(component, true, null);

    public static CloudStartupCheckResult Unhealthy(CloudStartupComponent component, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An unhealthy result requires a reason.", nameof(reason));
        }

        return new CloudStartupCheckResult(component, false, reason);
    }
}
