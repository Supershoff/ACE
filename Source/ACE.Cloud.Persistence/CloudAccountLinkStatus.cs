namespace ACE.Cloud.Persistence;

/// <summary>The lifecycle of one <see cref="CloudAccountLink"/> row (AUTH-005).</summary>
public enum CloudAccountLinkStatus
{
    /// <summary>The source account's future deposits currently route to the group's Main Account.</summary>
    Active,

    /// <summary>
    /// The link has ended (AUTH-005: unlinking never restores prior ownership; future deposits
    /// belong to the newly independent account). The row is retained for audit history.
    /// </summary>
    Unlinked,
}
