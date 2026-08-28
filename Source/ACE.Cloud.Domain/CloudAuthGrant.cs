namespace ACE.Cloud.Domain;

/// <summary>
/// A parsed, already-verified ACE Auth Bridge grant payload (AUTH-002): "a signed, audience-bound,
/// short-lived one-use grant." <see cref="Nonce"/> is the one-use identity a consumer records to
/// reject a replay; this type itself carries no consumption state, which is a persistence concern
/// the Cloud backend owns (the Auth Bridge that issues grants has no Cloud schema access at all,
/// ARCH-004).
/// </summary>
public sealed record CloudAuthGrant(uint AccountId, string Audience, DateTime IssuedAtUtc, DateTime ExpiresAtUtc, Guid Nonce);
