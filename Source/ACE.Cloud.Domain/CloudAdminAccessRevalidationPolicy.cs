namespace ACE.Cloud.Domain;

public enum CloudAdminAccessRevalidationOutcomeKind
{
    Authorized,
    InsufficientAccessLevel,
}

public sealed record CloudAdminAccessRevalidationResult(CloudAdminAccessRevalidationOutcomeKind Kind)
{
    public bool IsAuthorized => Kind == CloudAdminAccessRevalidationOutcomeKind.Authorized;

    public static CloudAdminAccessRevalidationResult Authorized() => new(CloudAdminAccessRevalidationOutcomeKind.Authorized);

    public static CloudAdminAccessRevalidationResult Denied() => new(CloudAdminAccessRevalidationOutcomeKind.InsufficientAccessLevel);
}

/// <summary>
/// ADM-001: "Admin means ACE ace_auth.account.accessLevel == 5. Revalidate on every sensitive
/// request; session claims alone are insufficient." This is the pure decision only -- callers must
/// source <paramref name="currentAccessLevel"/> from a fresh read of ace_auth.account for the
/// request in hand (the Auth Bridge's access-level endpoint), never from a cached session claim.
/// </summary>
public static class CloudAdminAccessRevalidationPolicy
{
    private const uint AdminAccessLevel = 5;

    public static CloudAdminAccessRevalidationResult Evaluate(uint currentAccessLevel) =>
        currentAccessLevel == AdminAccessLevel
            ? CloudAdminAccessRevalidationResult.Authorized()
            : CloudAdminAccessRevalidationResult.Denied();
}
