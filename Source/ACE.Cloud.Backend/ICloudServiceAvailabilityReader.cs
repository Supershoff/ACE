using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;

namespace ACE.Cloud.Backend;

/// <summary>
/// The one fact issue #33's Withdrawal Token creation endpoint needs from
/// <see cref="CloudStartupDiagnosticsService"/> (ARCH-008, WDR-008): the current
/// <see cref="CloudServiceAvailabilityMode"/>, so it can refuse specifically while the ACE world
/// process is unavailable even though the rest of the Cloud Mule web app stays healthy and routable.
/// Interface-extracted for the same reason as <c>ICloudAccountOwnershipResolver</c>: so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute a fixed-mode fake instead of exercising
/// <see cref="CloudStartupDiagnosticsService"/>'s real database/world-boundary probes.
/// </summary>
public interface ICloudServiceAvailabilityReader
{
    Task<CloudServiceAvailabilityMode> GetCurrentModeAsync(CancellationToken cancellationToken = default);
}

public sealed class CloudServiceAvailabilityReader : ICloudServiceAvailabilityReader
{
    private readonly CloudStartupDiagnosticsService _diagnostics;

    public CloudServiceAvailabilityReader(CloudStartupDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public async Task<CloudServiceAvailabilityMode> GetCurrentModeAsync(CancellationToken cancellationToken = default)
    {
        var report = await _diagnostics.EvaluateAsync(cancellationToken).ConfigureAwait(false);
        return report.Mode;
    }
}
