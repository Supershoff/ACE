namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of <see cref="CloudDisplayCharacterSelectionPolicy.SelectDefault"/>: either an
/// immutable snapshot of the winning candidate at the moment of selection (AUTH-003: "audit records
/// retain immutable IDs and name snapshots"), or <see cref="HasSelection"/> false when the Main/
/// Linked group currently has no current character at all (the "no-current-character" case).
/// </summary>
public sealed record CloudDisplayCharacterSelectionResult
{
    public bool HasSelection { get; }

    public uint CharacterId { get; }

    public string? CharacterName { get; }

    public int TotalLogins { get; }

    private CloudDisplayCharacterSelectionResult(bool hasSelection, uint characterId, string? characterName, int totalLogins)
    {
        HasSelection = hasSelection;
        CharacterId = characterId;
        CharacterName = characterName;
        TotalLogins = totalLogins;
    }

    public static CloudDisplayCharacterSelectionResult Selected(CloudDisplayCharacterCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new CloudDisplayCharacterSelectionResult(true, candidate.CharacterId, candidate.CharacterName, candidate.TotalLogins);
    }

    public static CloudDisplayCharacterSelectionResult None() => new(false, characterId: 0, characterName: null, totalLogins: 0);
}
