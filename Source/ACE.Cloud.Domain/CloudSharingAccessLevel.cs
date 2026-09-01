namespace ACE.Cloud.Domain;

/// <summary>
/// The <em>effective</em> access one viewer currently has to one owner's personal Cloud Inventory,
/// after composing owner identity, an explicit individual <see cref="CloudSharingGrantLevel"/>, and
/// guild(allegiance)-derived access in that documented precedence (issue #36's Outcome; SHARE-004).
/// Distinct from <see cref="CloudSharingGrantLevel"/>: a grant can only ever record None/ViewOnly/
/// ViewAndWithdraw, but the effective access a viewer ends up with also includes <see cref="Owner"/>,
/// which is never itself a storable grant.
/// </summary>
public enum CloudSharingAccessLevel
{
    /// <summary>No access: no explicit grant, no qualifying derived access, and not the owner.</summary>
    None,

    /// <summary>View-only access, whether from an explicit grant or from guild-derived access.</summary>
    ViewOnly,

    /// <summary>View access plus Withdrawal Token creation. Only ever the result of an explicit grant (guild-derived access never reaches this tier).</summary>
    ViewAndWithdraw,

    /// <summary>The asset owner themselves: full authority, not mediated by any Sharing Grant.</summary>
    Owner,
}
