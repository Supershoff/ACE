namespace ACE.Cloud.Domain;

/// <summary>
/// Every fact <see cref="CloudSharingGrantPolicy.EvaluateSet"/> needs to decide one Sharing Grant
/// change (SHARE-001, SHARE-004). The caller (the Cloud Transaction Authority gateway) gathers each
/// fact under its own locked commit-time revalidation; this type carries no database access of its
/// own, keeping the eligibility decision itself pure and independently testable (mirrors
/// <see cref="CloudAccountLinkRequest"/>'s own established shape).
/// </summary>
public sealed record CloudSharingGrantSetRequest
{
    public CloudAccountId OwnerAccountId { get; }

    /// <summary>True when the owner's typed current character name resolved to a live character (SHARE-001).</summary>
    public bool GranteeCharacterFound { get; }

    /// <summary>The resolved grantee's effective Main Account ID, or null when <see cref="GranteeCharacterFound"/> is false.</summary>
    public CloudAccountId? GranteeAccountId { get; }

    /// <summary>True when the resolved grantee character belongs to a different Cloud Shard (ARCH-001).</summary>
    public bool GranteeIsCrossShard { get; }

    public CloudSharingGrantLevel RequestedLevel { get; }

    public CloudMutationGateState MutationGateState { get; }

    public CloudSharingGrantSetRequest(
        CloudAccountId ownerAccountId,
        bool granteeCharacterFound,
        CloudAccountId? granteeAccountId,
        bool granteeIsCrossShard,
        CloudSharingGrantLevel requestedLevel,
        CloudMutationGateState mutationGateState)
    {
        ArgumentNullException.ThrowIfNull(ownerAccountId);

        OwnerAccountId = ownerAccountId;
        GranteeCharacterFound = granteeCharacterFound;
        GranteeAccountId = granteeAccountId;
        GranteeIsCrossShard = granteeIsCrossShard;
        RequestedLevel = requestedLevel;
        MutationGateState = mutationGateState;
    }
}
