namespace ACE.Cloud.AuthBridge;

/// <summary>
/// This deployment's configuration, bound from the "AuthBridge" configuration section. <see
/// cref="AceAuthConnectionString"/> must authenticate as a narrowly privileged identity scoped to
/// read the <c>ace_auth.account</c> password fields only -- AUTH-002: "The Cloud backend never
/// stores passwords, logs them, or implements password-hash verification"; this bridge reuses ACE's
/// own verifier instead of inventing one.
/// </summary>
public sealed class AuthBridgeOptions
{
    public const string SectionName = "AuthBridge";

    public required string AceAuthConnectionString { get; init; }

    public required string ComponentVersion { get; init; }

    public required Uri WorldBoundaryHealthEndpoint { get; init; }

    /// <summary>
    /// How long an issued grant remains valid (AUTH-002: "short-lived one-use grant"). Kept short:
    /// the Cloud backend is expected to exchange a grant for a session within the same request that
    /// requested it.
    /// </summary>
    public int GrantTimeToLiveSeconds { get; init; } = 30;

    /// <summary>The symmetric key currently used to sign new grants and validate incoming private-service requests.</summary>
    public required string ActiveServiceKeyId { get; init; }

    /// <summary>Base64-encoded secret for <see cref="ActiveServiceKeyId"/>.</summary>
    public required string ActiveServiceKeySecret { get; init; }

    /// <summary>
    /// The key ID a rotation just retired, if any (security baseline: "support key rotation"). A
    /// request/grant signed with this key still authenticates during the deployment's overlap
    /// window; nothing new is ever signed with it.
    /// </summary>
    public string? PreviousServiceKeyId { get; init; }

    /// <summary>Base64-encoded secret for <see cref="PreviousServiceKeyId"/>, required together with it.</summary>
    public string? PreviousServiceKeySecret { get; init; }

    /// <summary>How far a signed private-service request's timestamp may drift from this host's clock before it is rejected.</summary>
    public int PrivateServiceRequestMaxClockSkewSeconds { get; init; } = 30;

    /// <summary>Maximum login attempts a single account name or source IP may make within <see cref="LoginRateLimitWindowSeconds"/>.</summary>
    public int MaxLoginAttemptsPerWindow { get; init; } = 10;

    public int LoginRateLimitWindowSeconds { get; init; } = 60;
}
