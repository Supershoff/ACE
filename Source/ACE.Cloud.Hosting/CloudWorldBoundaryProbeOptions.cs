namespace ACE.Cloud.Hosting;

/// <summary>
/// Where and how long to wait when probing ACE's private world-boundary health endpoint
/// (<see cref="HttpCloudWorldBoundaryHealthProbe"/>). The endpoint itself is bound privately and
/// never exposed publicly (Security baseline: "Do not expose these endpoints publicly").
/// </summary>
public sealed class CloudWorldBoundaryProbeOptions
{
    public required Uri HealthEndpoint { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(3);
}
