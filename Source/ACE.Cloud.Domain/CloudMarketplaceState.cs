namespace ACE.Cloud.Domain;

/// <summary>
/// Marketplace State (MKT-203, MKT-204): the administrator-controlled operating mode of the auction
/// marketplace, distinct from <see cref="CloudGlobalMaintenanceState"/> (which pauses every Cloud
/// mutation) and from <c>CloudCustodianConfiguration.MarketplaceEnabled</c> (the shared Custodian
/// spawn location toggle, DEP-007/DEP-008 -- an unrelated concept that happens to share the word
/// "Marketplace").
/// </summary>
public enum CloudMarketplaceState
{
    /// <summary>Permits all Marketplace actions, including new listings.</summary>
    Enabled,

    /// <summary>Blocks only new listings; existing auctions may still bid, use Buy It Now, close, and settle.</summary>
    Disabled,

    /// <summary>Blocks all Marketplace mutations and clock progress. There is no separate Draining state.</summary>
    MaintenanceFrozen,
}
