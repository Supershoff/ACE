using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single persisted row holding the shard-wide `withdraw anywhere` bypass toggle and
/// configuration version for Withdrawal Landblocks (WDR-006, ADM-003). One row per deployment
/// (ARCH-001), matching <see cref="CloudShardBinding"/>'s established singleton shape. Named
/// landblocks live separately in <see cref="CloudWithdrawalNamedLandblockRecord"/> so adding or
/// removing one never contends on this row beyond the version bump every change already needs.
/// </summary>
public sealed class CloudWithdrawalLocationConfigurationRecord
{
    private CloudWithdrawalLocationConfigurationRecord()
    {
    }

    /// <summary>Bootstraps the out-of-the-box row (WDR-006: "defaults off") the first time a shard's configuration is read.</summary>
    public static CloudWithdrawalLocationConfigurationRecord CreateDefault(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Withdrawal Location configuration row requires a Cloud Shard ID.", nameof(shardId));
        }

        return new CloudWithdrawalLocationConfigurationRecord
        {
            Id = 1,
            ShardId = shardId,
            WithdrawAnywhereEnabled = false,
            Version = 1,
        };
    }

    public int Id { get; private set; } = 1;

    public string ShardId { get; private set; } = null!;

    public bool WithdrawAnywhereEnabled { get; private set; }

    /// <summary>Optimistic concurrency token (ARCH-006).</summary>
    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Converts this row plus its sibling named-landblock rows into the pure domain aggregate
    /// <see cref="CloudWithdrawalLocationConfigurationPolicy"/> operates on.
    /// </summary>
    public CloudWithdrawalLocationConfiguration ToDomain(IReadOnlyList<CloudWithdrawalNamedLandblockRecord> namedLandblocks)
    {
        ArgumentNullException.ThrowIfNull(namedLandblocks);

        var parsed = namedLandblocks
            .Select(row => new CloudWithdrawalNamedLandblock(row.Id, row.Landblock, row.Name))
            .ToList();

        return new CloudWithdrawalLocationConfiguration(WithdrawAnywhereEnabled, parsed, new CloudAggregateVersion(Version));
    }

    /// <summary>
    /// Applies a validated <see cref="CloudWithdrawalLocationConfigurationPolicy"/> result's scalar
    /// fields to this row. Callers must hold this row's lock for the whole boundary transaction and
    /// separately reconcile <see cref="CloudWithdrawalNamedLandblockRecord"/> rows against
    /// <paramref name="domain"/>.NamedLandblocks themselves.
    /// </summary>
    internal void ApplyScalars(CloudWithdrawalLocationConfiguration domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        WithdrawAnywhereEnabled = domain.WithdrawAnywhereEnabled;
        Version = domain.Version.Value;
    }
}
