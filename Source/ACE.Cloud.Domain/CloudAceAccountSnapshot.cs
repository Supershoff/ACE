namespace ACE.Cloud.Domain;

/// <summary>
/// The exact <c>ace_auth.account</c> fields the ACE Auth Bridge needs to verify a login and apply
/// ACE's existing ban rule (AUTH-002), read through its own narrowly privileged, read-only identity
/// (<c>GRANT SELECT ON ace_auth.account</c>) rather than ACE.Database's full read/write repository.
/// </summary>
public sealed record CloudAceAccountSnapshot(
    uint AccountId,
    string AccountName,
    string PasswordHash,
    string PasswordSalt,
    uint AccessLevel,
    DateTime? BanExpireTime,
    string? BanReason);
