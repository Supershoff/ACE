namespace ACE.Cloud.Domain;

/// <summary>
/// Pure state-machine rule for the core custody state model's "immediate cloud transfer" edge: an
/// off-world ownership change that is not itself the fulfillment of a typed reservation (for example
/// an Allegiance Vault contribution/take, or the currency leg of a completed marketplace
/// settlement). A target with an active exclusive reservation can never be transferred this way
/// (IMPLEMENTATION-BRIEF.md: a reservation "prevents its Cloud Items from being listed, transferred,
/// modified, or included in another withdrawal"); its owning workflow must release the reservation
/// first.
/// </summary>
public static class CloudOwnershipTransferPolicy
{
    public static CloudOwnershipTransferResult Transfer(
        CloudReservationTarget target,
        CloudAccountId currentOwnerId,
        CloudAccountId newOwnerId,
        CloudAggregateVersion currentVersion,
        CloudAggregateVersion expectedVersion,
        CloudReservationAllocation? activeAllocation,
        CloudMutationGateState gateState)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(currentOwnerId);
        ArgumentNullException.ThrowIfNull(newOwnerId);
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(expectedVersion);

        if (gateState == CloudMutationGateState.Frozen)
        {
            return CloudOwnershipTransferResult.Failure(
                CloudCustodyTransitionErrorKind.MutationsFrozen,
                "Cloud mutations are currently frozen by Global Cloud Maintenance or a Marketplace Maintenance Frozen state.");
        }

        if (activeAllocation is not null && activeAllocation.Status == CloudReservationStatus.Active)
        {
            return CloudOwnershipTransferResult.Failure(
                CloudCustodyTransitionErrorKind.TargetAlreadyReserved,
                $"{target} cannot change owner while it is exclusively reserved by {activeAllocation.Kind} reservation "
                    + $"{activeAllocation.ReservationId}.");
        }

        if (currentVersion != expectedVersion)
        {
            return CloudOwnershipTransferResult.Failure(
                CloudCustodyTransitionErrorKind.VersionConflict,
                $"{target} is at version {currentVersion}, not the expected version {expectedVersion}.");
        }

        if (newOwnerId == currentOwnerId)
        {
            return CloudOwnershipTransferResult.Failure(
                CloudCustodyTransitionErrorKind.InvalidRequest,
                "An ownership transfer requires a different owner from the current one.");
        }

        return CloudOwnershipTransferResult.Success(newOwnerId, currentVersion.Next());
    }
}
