namespace ACE.Cloud.Domain;

/// <summary>
/// The kind of authoritative ACE character/allegiance change a <c>CloudCharacterIdentityEventPayload</c>
/// or <c>CloudAllegianceEventPayload</c> announces (ARCH-007's Custody Outbox pattern applied
/// to identity/allegiance seams rather than custody boundary handoffs). AUTH-003 needs
/// <see cref="CharacterRenamed"/>/<see cref="CharacterDeleted"/> to refresh a Display Character
/// selection; VAULT-001/VAULT-004 need the allegiance events to refresh vault membership/monarch
/// projections without the companion becoming their authority; <see cref="CharacterLoginObserved"/>
/// (issue #39's self-heal fix) is not a change at all -- it is an idempotent per-login snapshot that
/// repairs a projection row an older build's allegiance-only event may have left without an
/// account/name association, since ordinary login otherwise publishes no identity/allegiance event.
/// </summary>
public enum CloudIdentityEventType
{
    CharacterRenamed,
    CharacterDeleted,
    AllegianceSworn,
    AllegianceBroken,
    AllegianceMonarchChanged,

    /// <summary>
    /// Issue #39's self-heal fix: published after every successful world login (while AC Cloud Mule is
    /// enabled) with the character's current account/name/login/monarch snapshot. Never implies a
    /// rename, deletion, or allegiance change -- see <c>CloudCharacterIdentityReadProjection</c>
    /// and <c>CloudIdentityEventManager.PublishCharacterLoginObserved</c>.
    /// </summary>
    CharacterLoginObserved,
}
