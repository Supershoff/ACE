namespace ACE.Cloud.Contracts;

/// <summary>
/// Whether a Cloud boundary transaction may proceed, and why it was refused when it may not.
/// </summary>
public sealed record CloudMutationAuthorizationResult
{
    private CloudMutationAuthorizationResult(bool isAuthorized, string? reason)
    {
        IsAuthorized = isAuthorized;
        Reason = reason;
    }

    public bool IsAuthorized { get; }

    public string? Reason { get; }

    public static CloudMutationAuthorizationResult Authorized() => new(true, null);

    public static CloudMutationAuthorizationResult Refused(string reason) => new(false, reason);
}
