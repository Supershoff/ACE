namespace ACE.Cloud.Domain;

/// <summary>
/// The kind of authoritative ACE character/allegiance change a <c>CloudCharacterIdentityEventPayload</c>
/// or <c>CloudAllegianceEventPayload</c> announces (ARCH-007's Custody Outbox pattern applied
/// to identity/allegiance seams rather than custody boundary handoffs). AUTH-003 needs
/// <see cref="CharacterRenamed"/>/<see cref="CharacterDeleted"/> to refresh a Display Character
/// selection; VAULT-001/VAULT-004 need the allegiance events to refresh vault membership/monarch
/// projections without the companion becoming their authority.
/// </summary>
public enum CloudIdentityEventType
{
    CharacterRenamed,
    CharacterDeleted,
    AllegianceSworn,
    AllegianceBroken,
    AllegianceMonarchChanged,
}
