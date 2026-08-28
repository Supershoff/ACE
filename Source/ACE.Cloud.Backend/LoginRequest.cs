namespace ACE.Cloud.Backend;

public sealed record LoginRequest(string AccountName, string Password);

/// <summary>The freshly minted CSRF token the client must echo back (as a header) on subsequent state-changing requests -- the session cookie itself is HttpOnly and unreadable to client script.</summary>
public sealed record LoginResponse(string CsrfToken);
