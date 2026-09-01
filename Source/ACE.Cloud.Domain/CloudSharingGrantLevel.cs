namespace ACE.Cloud.Domain;

/// <summary>
/// The only two settable tiers a personal Sharing Grant can explicitly hold, plus the explicit
/// denial that overrides derived access (SHARE-002: "Personal Sharing Grants have only two access
/// levels: View Only and View & Withdraw"; SHARE-004: "An explicit individual Sharing Grant,
/// including None, overrides guild-derived personal-inventory access"). There is deliberately no
/// View + Deposit tier (CONTEXT.md's flagged ambiguity: "the View + Deposit tier duplicated Transfer
/// Offers").
/// </summary>
public enum CloudSharingGrantLevel
{
    /// <summary>An explicit denial: overrides any derived (allegiance) access immediately (SHARE-004).</summary>
    None,

    /// <summary>View-only access to the owner's personal Cloud Inventory and Full Cloud Appraisal.</summary>
    ViewOnly,

    /// <summary>
    /// View access plus the ability to create Withdrawal Tokens for the grantee's own Main/Linked
    /// group (SHARE-003). Grants no other mutation authority.
    /// </summary>
    ViewAndWithdraw,
}
