namespace ACE.Cloud.Domain;

/// <summary>
/// The single administrator-controlled <see cref="CloudMarketplaceState"/> for this deployment
/// (MKT-203, MKT-204). One row per deployment (ARCH-001), matching every other Cloud singleton
/// admin-config aggregate (<c>CloudCustodianConfiguration</c>, <see cref="CloudGlobalMaintenanceState"/>).
/// </summary>
public sealed record CloudMarketplaceConfiguration(CloudMarketplaceState State, CloudAggregateVersion Version)
{
    public CloudAggregateVersion Version { get; init; } = Version ?? throw new ArgumentNullException(nameof(Version));

    /// <summary>Out-of-the-box state: the Marketplace is enabled.</summary>
    public static CloudMarketplaceConfiguration Default() => new(CloudMarketplaceState.Enabled, CloudAggregateVersion.Initial);
}
