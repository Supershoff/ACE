using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single persisted row holding the shared Marketplace/Mansion toggles and configuration
/// version for this deployment's Cloud Custodians (DEP-007, DEP-008, ADM-003). One row per
/// deployment (ARCH-001), matching <see cref="CloudShardBinding"/>'s established singleton shape.
/// Custom positions live separately in <see cref="CloudCustodianCustomPositionRecord"/> so adding or
/// removing one never contends on this row beyond the version bump every change already needs.
/// </summary>
public sealed class CloudCustodianConfigurationRecord
{
    private CloudCustodianConfigurationRecord()
    {
    }

    /// <summary>
    /// Bootstraps the out-of-the-box row (DEP-007: "Default Custodian locations are every mansion
    /// and Marketplace") the first time a shard's configuration is read.
    /// </summary>
    public static CloudCustodianConfigurationRecord CreateDefault(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Custodian configuration row requires a Cloud Shard ID.", nameof(shardId));
        }

        return new CloudCustodianConfigurationRecord
        {
            Id = 1,
            ShardId = shardId,
            MarketplaceEnabled = true,
            MansionsEnabled = true,
            Version = 1,
        };
    }

    public int Id { get; private set; } = 1;

    public string ShardId { get; private set; } = null!;

    public bool MarketplaceEnabled { get; private set; }

    public bool MansionsEnabled { get; private set; }

    /// <summary>Optimistic concurrency token; also the "configuration version" a Custodian sell window is revalidated against (ARCH-006).</summary>
    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Converts this row plus its sibling custom-position rows into the pure domain aggregate
    /// <see cref="CloudCustodianConfigurationPolicy"/> operates on.
    /// </summary>
    public CloudCustodianConfiguration ToDomain(IReadOnlyList<CloudCustodianCustomPositionRecord> customPositions)
    {
        ArgumentNullException.ThrowIfNull(customPositions);

        var parsed = customPositions
            .Select(row => new CloudCustodianCustomPosition(row.Id, CloudCustodianPosition.TryParse(row.PositionRaw)
                ?? throw new InvalidOperationException($"Persisted custom Custodian position {row.Id} is no longer a valid ACE position string: \"{row.PositionRaw}\".")))
            .ToList();

        return new CloudCustodianConfiguration(MarketplaceEnabled, MansionsEnabled, parsed, new CloudAggregateVersion(Version));
    }

    /// <summary>
    /// Applies a validated <see cref="CloudCustodianConfigurationPolicy"/> result's scalar fields to
    /// this row. Callers must hold this row's lock for the whole boundary transaction and separately
    /// reconcile <see cref="CloudCustodianCustomPositionRecord"/> rows against
    /// <paramref name="domain"/>.CustomPositions themselves.
    /// </summary>
    internal void ApplyScalars(CloudCustodianConfiguration domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        MarketplaceEnabled = domain.MarketplaceEnabled;
        MansionsEnabled = domain.MansionsEnabled;
        Version = domain.Version.Value;
    }
}
