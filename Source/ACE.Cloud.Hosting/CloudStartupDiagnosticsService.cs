namespace ACE.Cloud.Hosting;

/// <summary>
/// Runs a companion service's configured startup/health checks in order and stops at the first
/// unhealthy one (OPS-002: "identify the incompatible or unavailable component precisely"). Ordering
/// matters: <see cref="CloudStartupChecks.Database"/> (or <see cref="CloudStartupChecks.RawConnectionAvailability"/>)
/// must run first so a database outage is reported as exactly one <see cref="CloudStartupComponent.Database"/>
/// failure instead of a confusing cascade of every check downstream also failing.
/// </summary>
public sealed class CloudStartupDiagnosticsService
{
    private readonly IReadOnlyList<Func<CancellationToken, Task<CloudStartupCheckResult>>> _checks;

    public CloudStartupDiagnosticsService(IEnumerable<Func<CancellationToken, Task<CloudStartupCheckResult>>> checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        _checks = checks.ToList();
    }

    public async Task<CloudStartupDiagnosticsReport> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<CloudStartupCheckResult>();

        foreach (var check in _checks)
        {
            var result = await check(cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!result.IsHealthy)
            {
                return new CloudStartupDiagnosticsReport(ModeFor(result.Component), results);
            }
        }

        return new CloudStartupDiagnosticsReport(CloudServiceAvailabilityMode.Operational, results);
    }

    private static CloudServiceAvailabilityMode ModeFor(CloudStartupComponent component) => component switch
    {
        CloudStartupComponent.Database => CloudServiceAvailabilityMode.ReadOnly,
        CloudStartupComponent.WorldBoundary => CloudServiceAvailabilityMode.WorldBoundaryUnavailable,
        CloudStartupComponent.ShardIdentity or CloudStartupComponent.SchemaMigration or CloudStartupComponent.ContractProtocol =>
            CloudServiceAvailabilityMode.VersionIncompatible,
        _ => throw new ArgumentOutOfRangeException(nameof(component), component, "Unrecognized startup component."),
    };
}
