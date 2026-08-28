namespace ACE.Cloud.Domain;

/// <summary>
/// AUTH-003's default Display Character selection: "the current character with the highest
/// total_Logins across the Main and Linked Accounts." Deleting or renaming the selected character
/// re-runs this same policy against the group's refreshed current-character candidate list -- a
/// rename simply changes that character's snapshot name in the list, while a deletion removes it
/// entirely -- rather than needing a separate fallback algorithm.
/// </summary>
public static class CloudDisplayCharacterSelectionPolicy
{
    /// <summary>
    /// Selects the highest-<see cref="CloudDisplayCharacterCandidate.TotalLogins"/> candidate.
    /// Ties are broken deterministically by the lowest <see cref="CloudDisplayCharacterCandidate.CharacterId"/>
    /// (the earliest-created character), never by input/enumeration order, matching every other
    /// deterministic tiebreak already established in this domain (for example
    /// <see cref="CloudBidPriorityPolicy"/>'s commit-order tiebreak). An empty candidate list (no
    /// current character anywhere in the Main/Linked group) returns <see cref="CloudDisplayCharacterSelectionResult.None"/>.
    /// </summary>
    public static CloudDisplayCharacterSelectionResult SelectDefault(IReadOnlyList<CloudDisplayCharacterCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
        {
            return CloudDisplayCharacterSelectionResult.None();
        }

        var winner = candidates
            .OrderByDescending(candidate => candidate.TotalLogins)
            .ThenBy(candidate => candidate.CharacterId)
            .First();

        return CloudDisplayCharacterSelectionResult.Selected(winner);
    }
}
