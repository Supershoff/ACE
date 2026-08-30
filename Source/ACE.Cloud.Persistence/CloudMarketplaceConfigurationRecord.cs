using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single persisted row holding this deployment's <see cref="CloudMarketplaceState"/> (MKT-203,
/// MKT-204). One row per deployment (ARCH-001).
/// </summary>
public sealed class CloudMarketplaceConfigurationRecord
{
    private CloudMarketplaceConfigurationRecord()
    {
    }

    public static CloudMarketplaceConfigurationRecord CreateDefault(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Marketplace State row requires a Cloud Shard ID.", nameof(shardId));
        }

        return new CloudMarketplaceConfigurationRecord
        {
            Id = 1,
            ShardId = shardId,
            State = CloudMarketplaceState.Enabled,
            Version = 1,
        };
    }

    public int Id { get; private set; } = 1;

    public string ShardId { get; private set; } = null!;

    public CloudMarketplaceState State { get; private set; }

    /// <summary>Optimistic concurrency token (ARCH-006).</summary>
    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public CloudMarketplaceConfiguration ToDomain() => new(State, new CloudAggregateVersion(Version));

    internal void ApplyScalars(CloudMarketplaceConfiguration domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        State = domain.State;
        Version = domain.Version.Value;
    }
}
