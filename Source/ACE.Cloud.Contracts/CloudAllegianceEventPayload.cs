using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// An allegiance membership change published from an authoritative ACE seam (VAULT-001, ARCH-007):
/// swearing in, breaking away, or a monarch change (including the vassals-become-monarchs case a
/// monarch's own deletion can produce). The companion web uses this to refresh which Acting
/// Characters currently have Allegiance Vault access without maintaining a parallel membership
/// roster of its own (CONTEXT.md: "Membership derives live from ACE's allegiance tree. Do not create
/// a parallel guild roster."). Carries no <see cref="ICloudPublicContract"/> marker.
/// </summary>
/// <remarks>
/// Issue #39's oath-first fix: this payload also carries the authoritative account/name/login
/// snapshot ACE already holds in memory for the character at the moment of the event, exactly like
/// a character rename/deletion event does. Without it, a fresh/rebuilt Cloud database whose very
/// first identity event for a character happens to be an allegiance event (no prior rename/login
/// event) would produce a projection row with a populated MonarchId but a null AccountId, making the
/// resulting Acting Character invisible to its own account. Publishing the snapshot on every
/// allegiance event -- not only the first -- keeps the projection correct under any event ordering.
/// </remarks>
public sealed record CloudAllegianceEventPayload
{
    public uint CharacterId { get; }

    public CloudIdentityEventType EventType { get; }

    /// <summary>This character's monarch immediately after the event, or null if they now have none.</summary>
    public uint? MonarchId { get; }

    /// <summary>This character's monarch immediately before the event, or null if they had none.</summary>
    public uint? PriorMonarchId { get; }

    /// <summary>The character's owning ACE account ID, authoritative as of this event.</summary>
    public uint AccountId { get; }

    /// <summary>The character's current name, authoritative as of this event.</summary>
    public string CharacterName { get; }

    /// <summary>The character's current total-logins count, authoritative as of this event.</summary>
    public int TotalLogins { get; }

    public CloudAllegianceEventPayload(
        uint characterId,
        CloudIdentityEventType eventType,
        uint? monarchId,
        uint? priorMonarchId,
        uint accountId,
        string characterName,
        int totalLogins)
    {
        if (characterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), "An allegiance event requires a real character GUID.");
        }

        if (eventType != CloudIdentityEventType.AllegianceSworn
            && eventType != CloudIdentityEventType.AllegianceBroken
            && eventType != CloudIdentityEventType.AllegianceMonarchChanged)
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), $"{eventType} is not an allegiance event type.");
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "An allegiance event requires a real account ID.");
        }

        if (string.IsNullOrWhiteSpace(characterName))
        {
            throw new ArgumentException("An allegiance event requires a character name snapshot.", nameof(characterName));
        }

        CharacterId = characterId;
        EventType = eventType;
        MonarchId = monarchId;
        PriorMonarchId = priorMonarchId;
        AccountId = accountId;
        CharacterName = characterName;
        TotalLogins = totalLogins;
    }
}
