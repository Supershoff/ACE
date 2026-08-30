namespace ACE.Cloud.Domain;

/// <summary>
/// Pure rules over <see cref="CloudMarketplaceState"/> (MKT-203, MKT-204). The three query predicates
/// below are the exact vocabulary a future listing/auction engine gates on; they are established here,
/// against this issue's tests, so that engine only needs to call them rather than re-deriving the
/// Enabled/Disabled/MaintenanceFrozen rules.
/// </summary>
public static class CloudMarketplaceStatePolicy
{
    public static CloudMarketplaceConfigurationChangeResult SetState(
        CloudMarketplaceConfiguration current, CloudMarketplaceState requested, uint actorAccessLevel)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!CloudAdminAccessRevalidationPolicy.Evaluate(actorAccessLevel).IsAuthorized)
        {
            return CloudMarketplaceConfigurationChangeResult.Failure(
                "Only an administrator (ace_auth.account.accessLevel == 5) may change Marketplace State.");
        }

        if (current.State == requested)
        {
            return CloudMarketplaceConfigurationChangeResult.Success(current);
        }

        return CloudMarketplaceConfigurationChangeResult.Success(current with
        {
            State = requested,
            Version = current.Version.Next(),
        });
    }

    /// <summary>MKT-203: only Enabled permits publishing a new listing.</summary>
    public static bool CanPublishNewListing(CloudMarketplaceState state) => state == CloudMarketplaceState.Enabled;

    /// <summary>
    /// MKT-203/MKT-204: Enabled and Disabled both let an already-published auction bid, use Buy It
    /// Now, close, and settle; only MaintenanceFrozen blocks that activity too.
    /// </summary>
    public static bool CanContinueExistingAuctionActivity(CloudMarketplaceState state) => state != CloudMarketplaceState.MaintenanceFrozen;

    /// <summary>MKT-204: only MaintenanceFrozen pauses auction clock progress.</summary>
    public static bool BlocksClockProgress(CloudMarketplaceState state) => state == CloudMarketplaceState.MaintenanceFrozen;
}
