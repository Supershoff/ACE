namespace ACE.Cloud.AuthBridge;

/// <summary>The Cloud backend's request to verify Main Account credentials and issue a grant (AUTH-002).</summary>
public sealed record IssueGrantRequest(string AccountName, string Password, string Audience);

public sealed record IssueGrantResponse(string Grant, DateTime ExpiresAtUtc, uint AccountId, uint AccessLevel);

public sealed record AccessLevelResponse(uint AccountId, uint AccessLevel);
