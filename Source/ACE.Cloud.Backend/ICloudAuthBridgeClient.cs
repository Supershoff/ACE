namespace ACE.Cloud.Backend;

public enum CloudAuthBridgeGrantOutcomeKind
{
    Issued,
    InvalidCredentials,
    AccountBanned,
    RateLimited,
    Unavailable,
}

public sealed record CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind Kind, string? Grant);

/// <summary>Seam for the Cloud backend's calls to the private ACE Auth Bridge (AUTH-002), so endpoint tests can substitute a fake instead of a real Auth Bridge over HTTP.</summary>
public interface ICloudAuthBridgeClient
{
    Task<CloudAuthBridgeGrantResult> IssueGrantAsync(string accountName, string password, string audience, CancellationToken cancellationToken = default);

    /// <summary>ADM-001: a fresh <c>ace_auth.account.accessLevel</c> read for the given account, or null if the account no longer exists.</summary>
    Task<uint?> GetFreshAccessLevelAsync(uint accountId, CancellationToken cancellationToken = default);
}
