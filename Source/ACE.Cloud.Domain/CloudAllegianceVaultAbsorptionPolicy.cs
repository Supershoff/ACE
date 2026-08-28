namespace ACE.Cloud.Domain;

/// <summary>
/// Pure precondition check for VAULT-004's Vault Absorption: "When a monarch joins another
/// allegiance, atomically absorb the entire source vault into the destination vault, archive the
/// empty source, and preserve item provenance plus both vault identities" (CONTEXT.md line 213).
/// This policy only validates the preconditions; the actual item-by-item ownership transfer -- one
/// application of <see cref="CloudOwnershipTransferPolicy.Transfer"/> per Cloud Custody Record/Cloud
/// Stack Lot currently owned by <c>sourceVaultId</c> -- is the persistence layer's job, since it
/// requires enumerating those rows under lock.
/// </summary>
public static class CloudAllegianceVaultAbsorptionPolicy
{
    public static CloudAllegianceVaultAbsorptionResult Absorb(
        CloudAccountId sourceVaultId, CloudAccountId destinationVaultId, CloudMutationGateState gateState)
    {
        ArgumentNullException.ThrowIfNull(sourceVaultId);
        ArgumentNullException.ThrowIfNull(destinationVaultId);

        if (gateState == CloudMutationGateState.Frozen)
        {
            return CloudAllegianceVaultAbsorptionResult.Failure(
                CloudCustodyTransitionErrorKind.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (sourceVaultId == destinationVaultId)
        {
            return CloudAllegianceVaultAbsorptionResult.Failure(
                CloudCustodyTransitionErrorKind.InvalidRequest,
                "Vault Absorption requires a different source and destination Allegiance Vault.");
        }

        return CloudAllegianceVaultAbsorptionResult.Success();
    }
}
