namespace ACE.Cloud.Backend;

/// <summary>
/// AUTH-005..009's link request body. Deliberately carries no "Main Account name confirmation"
/// field: the browser already knows its own logged-in account name (it was just typed to sign in),
/// so exact-name-typing is a client-side anti-fat-finger confirmation, never a server-trusted
/// authorization fact -- the real proof of intent this endpoint verifies is
/// <see cref="SourcePassword"/>, re-checked against the private ACE Auth Bridge exactly like login
/// (AUTH-007).
/// </summary>
public sealed record AccountLinkRequest(string SourceAccountName, string SourcePassword);

public sealed record AccountUnlinkRequest(uint LinkedAccountId);
