namespace ACE.Cloud.Domain;

/// <summary>
/// Resolves the real <see cref="CloudMutationGateState"/> every custody/reservation/ownership-transfer
/// policy revalidates at commit time (transaction rule 9), replacing the hardcoded
/// <see cref="CloudMutationGateState.Open"/> every Cloud Transaction Authority call site used before
/// Global Cloud Maintenance and Marketplace State existed as real aggregates (see
/// <see cref="CloudMutationGateState"/>'s own doc comment). Frozen if either administrative aggregate
/// currently blocks mutation.
/// </summary>
public static class CloudMutationGatePolicy
{
    public static CloudMutationGateState Resolve(bool globalMaintenanceIsFrozen, CloudMarketplaceState marketplaceState) =>
        globalMaintenanceIsFrozen || marketplaceState == CloudMarketplaceState.MaintenanceFrozen
            ? CloudMutationGateState.Frozen
            : CloudMutationGateState.Open;
}
