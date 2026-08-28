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
}
