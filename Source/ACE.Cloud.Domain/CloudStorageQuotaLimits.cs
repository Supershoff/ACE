namespace ACE.Cloud.Domain;

/// <summary>
/// The shard-wide personal and Allegiance Vault Storage Quota limits (INV-004): "Storage is unlimited
/// by default. Admins may enable shard-wide personal and Allegiance Vault quotas measured as native
/// biotas plus projected materialized lots." Null means unlimited. One row per deployment (ARCH-001).
/// </summary>
public sealed record CloudStorageQuotaLimits(int? PersonalLimit, int? VaultLimit, CloudAggregateVersion Version)
{
    public CloudAggregateVersion Version { get; init; } = Version ?? throw new ArgumentNullException(nameof(Version));

    /// <summary>Out-of-the-box state: both scopes unlimited.</summary>
    public static CloudStorageQuotaLimits Default() => new(PersonalLimit: null, VaultLimit: null, CloudAggregateVersion.Initial);

    public int? LimitFor(CloudStorageQuotaScope scope) => scope switch
    {
        CloudStorageQuotaScope.Personal => PersonalLimit,
        CloudStorageQuotaScope.AllegianceVault => VaultLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unrecognized Storage Quota scope."),
    };
}
