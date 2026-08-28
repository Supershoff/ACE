namespace ACE.Cloud.Hosting;

/// <summary>
/// The result of evaluating every configured <see cref="CloudStartupComponent"/> check for one
/// companion service (Backend, Auth Bridge, or Worker). <see cref="Results"/> holds every check that
/// actually ran -- <see cref="CloudStartupDiagnosticsService"/> stops at the first unhealthy one, so
/// this list is never longer than "every healthy check before the failure, plus the failure itself".
/// </summary>
public sealed record CloudStartupDiagnosticsReport(CloudServiceAvailabilityMode Mode, IReadOnlyList<CloudStartupCheckResult> Results)
{
    public bool IsFullyOperational => Mode == CloudServiceAvailabilityMode.Operational;
}
