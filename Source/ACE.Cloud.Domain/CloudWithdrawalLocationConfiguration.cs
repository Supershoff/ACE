namespace ACE.Cloud.Domain;

/// <summary>
/// One administrator-named Withdrawal Landblock (WDR-006), identified by a stable row ID so editing
/// it (remove, then add its replacement) is always a distinguishable identity change, and stored in
/// the `0x123E` 16-bit landblock format CONTEXT.md specifies -- a whole landblock, never a coordinate
/// radius.
/// </summary>
public sealed record CloudWithdrawalNamedLandblock(Guid Id, ushort Landblock, string Name)
{
    public string Name { get; init; } = string.IsNullOrWhiteSpace(Name)
        ? throw new ArgumentException("A named Withdrawal Landblock requires a non-empty name.", nameof(Name))
        : Name;
}

/// <summary>
/// The versioned, administrator-controlled Withdrawal Landblock allowlist plus the shard-wide
/// `withdraw anywhere` bypass toggle (WDR-006, ADM-003). Immutable; every change goes through
/// <see cref="CloudWithdrawalLocationConfigurationPolicy"/> and produces a new instance with an
/// incremented <see cref="Version"/> (ARCH-006, transaction rule 3). Marketplace and housing/SlumLord
/// eligibility are not part of this configuration: WDR-006 makes them always-allowed defaults that
/// ACE.Server resolves directly from live world content, exactly like Custodian's Marketplace/Mansion
/// resolution.
/// </summary>
public sealed record CloudWithdrawalLocationConfiguration(
    bool WithdrawAnywhereEnabled,
    IReadOnlyList<CloudWithdrawalNamedLandblock> NamedLandblocks,
    CloudAggregateVersion Version)
{
    public IReadOnlyList<CloudWithdrawalNamedLandblock> NamedLandblocks { get; init; } =
        NamedLandblocks ?? throw new ArgumentNullException(nameof(NamedLandblocks));

    public CloudAggregateVersion Version { get; init; } = Version ?? throw new ArgumentNullException(nameof(Version));

    /// <summary>Out-of-the-box configuration: no named landblocks yet, withdraw-anywhere off (WDR-006: "defaults off").</summary>
    public static CloudWithdrawalLocationConfiguration Default() => new(
        WithdrawAnywhereEnabled: false, NamedLandblocks: [], CloudAggregateVersion.Initial);
}
