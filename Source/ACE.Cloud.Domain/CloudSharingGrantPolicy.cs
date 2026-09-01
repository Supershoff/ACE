namespace ACE.Cloud.Domain;

/// <summary>
/// Pure rules for personal Sharing Grants (SHARE-001..004, AUTH-008, WDR-002): validating one grant
/// change, and composing owner/explicit-grant/guild(allegiance)-derived access into one effective
/// access level in the documented precedence (issue #36's Outcome: "making explicit individual
/// grants -- including None -- override allegiance-derived personal access"). Every method here is a
/// pure function over its inputs, matching every other Cloud policy in this namespace
/// (<see cref="CloudAccountLinkPolicy"/>, <see cref="CloudTransferOfferPolicy"/>): it never queries or
/// mutates a database itself.
/// </summary>
public static class CloudSharingGrantPolicy
{
    /// <summary>
    /// Evaluates one Sharing Grant change. Checks run in a fixed precedence so retrying an identical,
    /// still-illegal request always reports the same exact reason: the mutation gate first, then the
    /// character-resolution facts (unknown/cross-shard) that make the request nonsensical
    /// independent of any existing grant state, then the self-grantee shape. A request naming
    /// <see cref="CloudSharingGrantLevel.None"/> is evaluated identically to any other level -- an
    /// explicit revocation is itself a real, auditable Sharing Grant change (SHARE-004), not a
    /// separate "delete" operation.
    /// </summary>
    public static CloudSharingGrantSetResult EvaluateSet(CloudSharingGrantSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MutationGateState == CloudMutationGateState.Frozen)
        {
            return CloudSharingGrantSetResult.Failure(
                CloudSharingGrantRejectionCode.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (!request.GranteeCharacterFound || request.GranteeAccountId is null)
        {
            return CloudSharingGrantSetResult.Failure(
                CloudSharingGrantRejectionCode.UnknownGranteeCharacter,
                "No current character matching the typed grantee name could be resolved.");
        }

        if (request.GranteeIsCrossShard)
        {
            return CloudSharingGrantSetResult.Failure(
                CloudSharingGrantRejectionCode.CrossShardGrantee,
                "The resolved grantee belongs to a different Cloud Shard; Sharing Grants never cross shards.");
        }

        if (request.GranteeAccountId == request.OwnerAccountId)
        {
            return CloudSharingGrantSetResult.Failure(
                CloudSharingGrantRejectionCode.SelfGrantee, "A Sharing Grant cannot be set for the owner's own ownership group.");
        }

        return CloudSharingGrantSetResult.Success(request.GranteeAccountId, request.RequestedLevel);
    }

    /// <summary>
    /// Composes the effective access one viewer has to one owner's personal Cloud Inventory
    /// (SHARE-004's documented precedence): the owner always has full authority; otherwise an
    /// explicit individual grant -- including <see cref="CloudSharingGrantLevel.None"/> -- wins over
    /// guild-derived access; otherwise qualifying current allegiance membership derives View Only;
    /// otherwise no access at all.
    ///
    /// Flagged interpretation (AGENTS.md: "stop and surface" a conflicting/underspecified planning
    /// assumption -- recorded here rather than guessed silently): CONTEXT.md never states which of
    /// the two Sharing Grant tiers (SHARE-002) guild-derived access resolves to. SHARE-004's second
    /// sentence ("invalidates unredeemed Withdrawal Tokens created through it") is textually
    /// consistent with derived access reaching View & Withdraw, but every comparable capability
    /// expansion elsewhere in this spec (account linking, AUTH-007) requires an explicit, prominently
    /// warned, deliberately confirmed owner action before it takes effect -- automatically granting
    /// every current (and future) fellow allegiance member standing authority to move items out of an
    /// owner's personal inventory, with no such confirmation, would be a materially larger, unwarned
    /// custody exposure than anything else this product does by default. This implementation
    /// therefore caps derived access at View Only (never View & Withdraw); only an explicit individual
    /// grant can ever reach View & Withdraw. A future product decision can raise this cap without
    /// changing this method's shape -- only its return value here.
    /// </summary>
    public static CloudSharingAccessLevel ResolveEffectiveAccess(
        bool isOwner, CloudSharingGrantLevel? explicitLevel, bool hasQualifyingDerivedAccess)
    {
        if (isOwner)
        {
            return CloudSharingAccessLevel.Owner;
        }

        if (explicitLevel is { } level)
        {
            return level switch
            {
                CloudSharingGrantLevel.None => CloudSharingAccessLevel.None,
                CloudSharingGrantLevel.ViewOnly => CloudSharingAccessLevel.ViewOnly,
                CloudSharingGrantLevel.ViewAndWithdraw => CloudSharingAccessLevel.ViewAndWithdraw,
                _ => throw new ArgumentOutOfRangeException(nameof(explicitLevel), $"Unrecognized Sharing Grant level {level}."),
            };
        }

        return hasQualifyingDerivedAccess ? CloudSharingAccessLevel.ViewOnly : CloudSharingAccessLevel.None;
    }

    /// <summary>
    /// The exact capability set one effective access level grants (SHARE-002, SHARE-003): every
    /// forbidden capability issue #36's Red tests enumerate (deposit, listing, bidding, settings,
    /// linking, offers, permission management) is false for every level except <see cref="CloudSharingAccessLevel.Owner"/>.
    /// </summary>
    public static CloudSharingCapabilities CapabilitiesFor(CloudSharingAccessLevel accessLevel) => accessLevel switch
    {
        CloudSharingAccessLevel.Owner => CloudSharingCapabilities.Owner,
        CloudSharingAccessLevel.ViewAndWithdraw => CloudSharingCapabilities.ViewAndWithdraw,
        CloudSharingAccessLevel.ViewOnly => CloudSharingCapabilities.ViewOnly,
        CloudSharingAccessLevel.None => CloudSharingCapabilities.None,
        _ => throw new ArgumentOutOfRangeException(nameof(accessLevel), $"Unrecognized Sharing access level {accessLevel}."),
    };
}
