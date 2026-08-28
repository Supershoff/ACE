namespace ACE.Cloud.Backend;

/// <summary>
/// This deployment's configuration, bound from the "CloudBackend" configuration section
/// (appsettings.json/environment variables/secrets -- never committed operator data, CLAUDE.md
/// "no secrets or DAT-derived assets are committed"). <see cref="CloudConnectionString"/> must
/// authenticate as the narrowly privileged companion identity ARCH-004 requires: full access to the
/// <c>ace_cloud</c> schema and none to native <c>ace_shard</c>/<c>ace_auth</c> tables.
/// </summary>
public sealed class CloudBackendOptions
{
    public const string SectionName = "CloudBackend";

    public required string CloudConnectionString { get; init; }

    public required string ExpectedAceExtensionVersion { get; init; }

    public required string ExpectedContractProtocolVersion { get; init; }

    public required Uri WorldBoundaryHealthEndpoint { get; init; }

    /// <summary>
    /// The MariaDB server version to assume when building <see cref="ACE.Cloud.Persistence.CloudDbContext"/>
    /// options, e.g. "11.4.2-mariadb". Fixed rather than auto-detected so this host can start (and
    /// report an honest "database unavailable" readiness result) even while the database is down;
    /// auto-detection requires a live connection at options-build time.
    /// </summary>
    public string DatabaseServerVersion { get; init; } = "11.4.2-mariadb";
}
