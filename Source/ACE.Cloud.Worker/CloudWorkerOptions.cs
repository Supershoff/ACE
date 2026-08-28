namespace ACE.Cloud.Worker;

/// <summary>
/// This deployment's configuration, bound from the "CloudWorker" configuration section.
/// <see cref="CloudConnectionString"/> must authenticate as the same narrowly privileged companion
/// identity ARCH-004 requires (full access to <c>ace_cloud</c>, none to native biota tables).
/// </summary>
public sealed class CloudWorkerOptions
{
    public const string SectionName = "CloudWorker";

    public required string CloudConnectionString { get; init; }

    public required string ExpectedAceExtensionVersion { get; init; }

    public required string ExpectedContractProtocolVersion { get; init; }

    public required Uri WorldBoundaryHealthEndpoint { get; init; }

    public string DatabaseServerVersion { get; init; } = "11.4.2-mariadb";

    public TimeSpan DiagnosticsInterval { get; init; } = TimeSpan.FromSeconds(30);
}
