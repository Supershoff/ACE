namespace ACE.Cloud.Domain;

/// <summary>
/// The single administrator-controlled Safe Regex Search toggle for this deployment (SRCH-001:
/// "Admin can disable regex independently"). One row per deployment (ARCH-001), matching every other
/// Cloud singleton admin-config aggregate (<see cref="CloudMarketplaceConfiguration"/>,
/// <c>CloudCustodianConfiguration</c>). Disabling this never touches ordinary text/property search --
/// see <see cref="CloudInventorySearchEngine"/>'s doc comment for why the two paths cannot interact.
/// </summary>
public sealed record CloudSearchConfiguration(bool RegexSearchEnabled, CloudAggregateVersion Version)
{
    public CloudAggregateVersion Version { get; init; } = Version ?? throw new ArgumentNullException(nameof(Version));

    /// <summary>Out-of-the-box state: Safe Regex Search is enabled.</summary>
    public static CloudSearchConfiguration Default() => new(RegexSearchEnabled: true, CloudAggregateVersion.Initial);
}
