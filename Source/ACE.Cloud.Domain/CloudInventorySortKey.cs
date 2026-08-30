namespace ACE.Cloud.Domain;

/// <summary>
/// A user-selectable AC-style inventory grid sort key (UI-003: "offers user-selectable sort keys").
/// Every key is combined with <see cref="CloudInventorySortDirection"/> and, as the final tie-break,
/// each item's stable identity (UI-003: "stable item identity as the final tie-break") by
/// <see cref="CloudMulePagePolicy"/>, so two items that compare equal on the chosen key never
/// nondeterministically swap Mule Page membership between requests.
/// </summary>
public enum CloudInventorySortKey
{
    Name,
    Value,
    Burden,
}
