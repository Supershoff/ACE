using ACE.Cloud.Hosting;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudWorldBoundaryHealthProbe"/> substitute, healthy by default.</summary>
internal sealed class FakeCloudWorldBoundaryHealthProbe : ICloudWorldBoundaryHealthProbe
{
    public bool IsHealthy { get; set; } = true;

    public Task<CloudStartupCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsHealthy
            ? CloudStartupCheckResult.Healthy(CloudStartupComponent.WorldBoundary)
            : CloudStartupCheckResult.Unhealthy(CloudStartupComponent.WorldBoundary, "ACE world process is offline (test)."));
}
