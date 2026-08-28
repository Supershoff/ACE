using ACE.Cloud.Domain;

namespace ACE.Cloud.Contracts;

/// <summary>
/// A character identity change published from an authoritative ACE seam (AUTH-003, ARCH-007):
/// rename or deletion. The companion web uses this to refresh its Display Character projection
/// (falling back to the remaining current character with the highest <see cref="TotalLogins"/>)
/// without becoming the authority for character identity itself -- ACE's own <c>character</c> table
/// remains authoritative. Carries no <see cref="ICloudPublicContract"/> marker; character rosters are
/// private to their owning account.
/// </summary>
public sealed record CloudCharacterIdentityEventPayload
{
    public uint CharacterId { get; }

    public uint AccountId { get; }

    public CloudIdentityEventType EventType { get; }

    /// <summary>The character's name at the moment this event was published (a point-in-time snapshot, not a live lookup).</summary>
    public string CharacterName { get; }

    /// <summary>The character's <c>total_Logins</c> at the moment this event was published (AUTH-003's Display Character tiebreaker).</summary>
    public int TotalLogins { get; }

    public CloudCharacterIdentityEventPayload(
        uint characterId, uint accountId, CloudIdentityEventType eventType, string characterName, int totalLogins)
    {
        if (characterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), "A character identity event requires a real character GUID.");
        }

        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A character identity event requires a real account ID.");
        }

        if (eventType != CloudIdentityEventType.CharacterRenamed && eventType != CloudIdentityEventType.CharacterDeleted)
        {
            throw new ArgumentOutOfRangeException(nameof(eventType), $"{eventType} is not a character identity event type.");
        }

        if (string.IsNullOrWhiteSpace(characterName))
        {
            throw new ArgumentException("A character identity event requires a character name snapshot.", nameof(characterName));
        }

        CharacterId = characterId;
        AccountId = accountId;
        EventType = eventType;
        CharacterName = characterName;
        TotalLogins = totalLogins;
    }
}
