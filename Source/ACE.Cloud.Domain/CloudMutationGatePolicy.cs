namespace ACE.Cloud.Domain;

/// <summary>
/// Resolves the real <see cref="CloudMutationGateState"/> every custody/reservation/ownership-transfer
/// policy revalidates at commit time (transaction rule 9), replacing the hardcoded
/// <see cref="CloudMutationGateState.Open"/> every Cloud Transaction Authority call site used before
/// Global Cloud Maintenance and Marketplace State existed as real aggregates (see
/// <see cref="CloudMutationGateState"/>'s own doc comment). Global Cloud Maintenance (ADM-004) and
/// Marketplace Maintenance Frozen (MKT-204) are orthogonal gates (IMPLEMENTATION-BRIEF.md): Global
/// Cloud Maintenance blocks every Cloud Transaction Authority mutation, marketplace-scoped or not,
/// while Marketplace Maintenance Frozen is scoped to "all Marketplace mutations and clock progress"
/// alone and must never widen to also block deposits, withdrawals, account linking, Allegiance Vault
/// Absorption, or ownership transfer.
/// </summary>
public static class CloudMutationGatePolicy
{
    /// <summary>ADM-004: the gate every non-marketplace Cloud Transaction Authority mutation revalidates.</summary>
    public static CloudMutationGateState ResolveGlobal(bool globalMaintenanceIsFrozen) =>
        globalMaintenanceIsFrozen ? CloudMutationGateState.Frozen : CloudMutationGateState.Open;

    /// <summary>
    /// ADM-004/MKT-204: the gate a Marketplace-scoped mutation (a listing, bid, settlement, or clock
    /// progress) revalidates -- frozen while either Global Cloud Maintenance or Marketplace
    /// Maintenance Frozen applies.
    /// </summary>
    public static CloudMutationGateState ResolveMarketplace(bool globalMaintenanceIsFrozen, CloudMarketplaceState marketplaceState) =>
        globalMaintenanceIsFrozen || marketplaceState == CloudMarketplaceState.MaintenanceFrozen
            ? CloudMutationGateState.Frozen
            : CloudMutationGateState.Open;
}
