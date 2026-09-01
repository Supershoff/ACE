namespace ACE.Cloud.Domain;

/// <summary>
/// Pure Acting Character authorization rules for Allegiance Vault contribute/take (issue #37:
/// VAULT-001, VAULT-002, VAULT-003, INV-004..006). CONTEXT.md: "Access to an Allegiance Vault is
/// evaluated through an eligible Acting Character, so membership on one character does not grant
/// unrelated alts access" and "An active Allegiance Vault grants every current member equal view,
/// contribute, and take privileges" -- there is deliberately no rank check anywhere in this policy:
/// once a character's own live current monarch is resolved, every member of that allegiance has
/// identical authority, matching every other Cloud policy in this namespace
/// (<see cref="CloudTransferOfferPolicy"/>, <see cref="CloudSharingGrantPolicy"/>): a pure function
/// over its inputs that never queries or mutates a database itself.
/// </summary>
public static class CloudAllegianceVaultActionPolicy
{
    /// <summary>
    /// Evaluates one contribute or take attempt's Acting Character authorization. Checks run in a
    /// fixed precedence so retrying an identical, still-illegal request always reports the same exact
    /// reason: the mutation gate first, then the character-resolution facts (unknown/not in an
    /// allegiance) that make the request nonsensical independent of any item state, then the
    /// destination's Storage Quota. The resolved <see cref="CloudAllegianceVaultActionResult.VaultMonarchId"/>
    /// is always the Acting Character's own live current monarch -- never a caller-supplied vault
    /// identity -- so one alt's membership can never be used to reach a different, unrelated vault.
    /// </summary>
    public static CloudAllegianceVaultActionResult AuthorizeActingCharacter(CloudAllegianceVaultActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MutationGateState == CloudMutationGateState.Frozen)
        {
            return CloudAllegianceVaultActionResult.Failure(
                CloudAllegianceVaultActionRejectionCode.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (!request.ActingCharacterFound)
        {
            return CloudAllegianceVaultActionResult.Failure(
                CloudAllegianceVaultActionRejectionCode.ActingCharacterNotFound,
                "No current character matching the requested Acting Character could be resolved.");
        }

        if (request.ActingCharacterCurrentMonarchId is not { } vaultMonarchId)
        {
            return CloudAllegianceVaultActionResult.Failure(
                CloudAllegianceVaultActionRejectionCode.ActingCharacterNotInAllegiance,
                "The Acting Character does not currently belong to any allegiance, so it has no Allegiance Vault to act for.");
        }

        var quota = CloudStorageQuotaPolicy.CheckNewObligation(request.DestinationQuotaLimit, request.DestinationCurrentProjectedCount);
        if (!quota.IsSuccess)
        {
            return CloudAllegianceVaultActionResult.Failure(CloudAllegianceVaultActionRejectionCode.DestinationOverQuota, quota.Reason!);
        }

        return CloudAllegianceVaultActionResult.Success(vaultMonarchId);
    }
}
