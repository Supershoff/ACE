namespace ACE.Cloud.Domain;

/// <summary>
/// One current (not deleted) character eligible for Display Character selection (AUTH-003): a
/// character belonging to the Main Account or any of its Linked Accounts. Callers exclude deleted
/// characters entirely before building this list rather than passing an "is current" flag, so a
/// character that no longer exists can never accidentally win a selection.
/// </summary>
public sealed record CloudDisplayCharacterCandidate
{
    public uint CharacterId { get; }

    public string CharacterName { get; }

    /// <summary>The character's <c>total_Logins</c> (AUTH-003's default-selection tiebreaker).</summary>
    public int TotalLogins { get; }

    public CloudDisplayCharacterCandidate(uint characterId, string characterName, int totalLogins)
    {
        if (characterId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(characterId), "A Display Character candidate requires a real character GUID.");
        }

        if (string.IsNullOrWhiteSpace(characterName))
        {
            throw new ArgumentException("A Display Character candidate requires a character name.", nameof(characterName));
        }

        if (totalLogins < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLogins), "A Display Character candidate's total logins cannot be negative.");
        }

        CharacterId = characterId;
        CharacterName = characterName;
        TotalLogins = totalLogins;
    }
}
