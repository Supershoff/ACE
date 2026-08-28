namespace ACE.Cloud.Domain;

/// <summary>
/// One administrator-managed custom Custodian Location (DEP-007), identified by a stable row ID so
/// editing it (remove, then add its replacement) is always a distinguishable identity change for
/// <see cref="CloudCustodianSpawnPlanner"/>, never an in-place mutation of a live location.
/// </summary>
public sealed record CloudCustodianCustomPosition(Guid Id, CloudCustodianPosition Position)
{
    public CloudCustodianPosition Position { get; init; } = Position ?? throw new ArgumentNullException(nameof(Position));
}

/// <summary>
/// The versioned, administrator-controlled set of Custodian Locations (DEP-007, DEP-008, ADM-003):
/// whether the shared Marketplace location and the shared Mansion set are each enabled, plus zero or
/// more administrator-added custom ACE positions. Immutable; every change goes through
/// <see cref="CloudCustodianConfigurationPolicy"/> and produces a new instance with an incremented
/// <see cref="Version"/> (ARCH-006, transaction rule 3), which is exactly what lets a stale open
/// Custodian sell window be detected later by comparing the version it opened under to this
/// aggregate's current one.
/// </summary>
public sealed record CloudCustodianConfiguration(
    bool MarketplaceEnabled,
    bool MansionsEnabled,
    IReadOnlyList<CloudCustodianCustomPosition> CustomPositions,
    CloudAggregateVersion Version)
{
    public IReadOnlyList<CloudCustodianCustomPosition> CustomPositions { get; init; } =
        CustomPositions ?? throw new ArgumentNullException(nameof(CustomPositions));

    public CloudAggregateVersion Version { get; init; } = Version ?? throw new ArgumentNullException(nameof(Version));

    /// <summary>
    /// Out-of-the-box configuration (DEP-007: "Default Custodian locations are every mansion and
    /// Marketplace"): both shared sets enabled, no custom positions yet.
    /// </summary>
    public static CloudCustodianConfiguration Default() => new(
        MarketplaceEnabled: true, MansionsEnabled: true, CustomPositions: [], CloudAggregateVersion.Initial);
}
