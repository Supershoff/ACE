namespace ACE.Cloud.Persistence;

/// <summary>Why a <see cref="CloudDisplayCharacterSelection"/> row changed (AUTH-003, EVT-002).</summary>
public enum CloudDisplayCharacterSelectionReason
{
    /// <summary>The group's first-ever selection (for example its first link, or its Main Account's first login).</summary>
    InitialSelection,

    /// <summary>The previously selected character was renamed; reselection re-ran against the refreshed roster.</summary>
    CharacterRenamed,

    /// <summary>The previously selected character was deleted; reselection re-ran against the refreshed roster.</summary>
    CharacterDeleted,

    /// <summary>The group's roster changed for another reason (for example a new link or unlink).</summary>
    RosterChanged,
}
