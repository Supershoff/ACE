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

    /// <summary>This deployment's immutable Cloud Shard ID (ARCH-001), matching the singleton CloudShardBinding row.</summary>
    public required string ShardId { get; init; }

    /// <summary>The private network base address of this deployment's ACE Auth Bridge (AUTH-002).</summary>
    public required Uri AuthBridgeBaseAddress { get; init; }

    /// <summary>The symmetric key currently used to sign private-service requests to the Auth Bridge and validate the grants it returns; must match the Auth Bridge's own active key.</summary>
    public required string ActiveServiceKeyId { get; init; }

    /// <summary>Base64-encoded secret for <see cref="ActiveServiceKeyId"/>.</summary>
    public required string ActiveServiceKeySecret { get; init; }

    /// <summary>The key ID a rotation just retired, if any; must match the Auth Bridge's own previous key during the overlap window.</summary>
    public string? PreviousServiceKeyId { get; init; }

    /// <summary>Base64-encoded secret for <see cref="PreviousServiceKeyId"/>, required together with it.</summary>
    public string? PreviousServiceKeySecret { get; init; }

    public int SessionTimeToLiveMinutes { get; init; } = 60;

    public string SessionCookieName { get; init; } = "ace_cloud_session";

    /// <summary>The exact origins (scheme + host + port) this deployment's web client is served from (security baseline: "strict origin policy").</summary>
    public required string[] AllowedOrigins { get; init; }

    /// <summary>Maximum login attempts a single source IP may make within <see cref="LoginRateLimitWindowSeconds"/>.</summary>
    public int MaxLoginAttemptsPerWindow { get; init; } = 20;

    public int LoginRateLimitWindowSeconds { get; init; } = 60;

    /// <summary>
    /// The same protected asset storage root the Worker's Asset Import pipeline writes composed icon
    /// derivatives under (ASSET-002/UI-006). The backend only ever reads the fixed
    /// <c>icon-cache/&lt;hex&gt;.png</c> namespace out of this root through
    /// <see cref="ACE.Cloud.Persistence.CloudIconDerivativeReader"/> -- see that type's doc comment
    /// for why this narrow read does not violate <see cref="ACE.Cloud.Persistence.IProtectedAssetBlobStore"/>'s
    /// "never reachable from a public route" guidance for every other use of that interface.
    /// </summary>
    public required string ProtectedAssetStorageRootDirectory { get; init; }
}
