namespace ACE.Cloud.Domain;

/// <summary>
/// The exhaustive set of exact reasons <see cref="CloudSharingGrantPolicy.EvaluateSet"/> can refuse a
/// Sharing Grant change (mirrors <see cref="CloudAccountLinkRejectionCode"/>'s own "exact response"
/// discipline for AUTH-005..009).
/// </summary>
public enum CloudSharingGrantRejectionCode
{
    /// <summary>Not a rejection; the request is approved.</summary>
    None,

    /// <summary>Global Cloud Maintenance or a Marketplace Maintenance Frozen state currently blocks mutation.</summary>
    MutationsFrozen,

    /// <summary>No current character matching the typed grantee name could be resolved.</summary>
    UnknownGranteeCharacter,

    /// <summary>The resolved grantee belongs to a different Cloud Shard; Sharing Grants never cross shards.</summary>
    CrossShardGrantee,

    /// <summary>The resolved grantee is the owner's own Main/Linked ownership group.</summary>
    SelfGrantee,
}
