namespace ACE.Cloud.Domain;

/// <summary>
/// The well-known audience values an ACE Auth Bridge grant (<see cref="CloudAuthGrant.Audience"/>)
/// may be bound to. A shared constant keeps the Auth Bridge (which stamps a grant's audience) and
/// the Cloud backend (which requires it match on exchange) from drifting apart on a bare string.
/// </summary>
public static class CloudAuthAudiences
{
    public const string CloudBackend = "cloud-backend";
}
