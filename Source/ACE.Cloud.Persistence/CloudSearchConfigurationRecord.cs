using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single persisted row holding this deployment's <see cref="CloudSearchConfiguration"/>
/// (SRCH-001: "Admin can disable regex independently"). One row per deployment (ARCH-001).
/// </summary>
public sealed class CloudSearchConfigurationRecord
{
    private CloudSearchConfigurationRecord()
    {
    }

    public static CloudSearchConfigurationRecord CreateDefault(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Safe Regex Search configuration row requires a Cloud Shard ID.", nameof(shardId));
        }

        return new CloudSearchConfigurationRecord
        {
            Id = 1,
            ShardId = shardId,
            RegexSearchEnabled = true,
            Version = 1,
        };
    }

    public int Id { get; private set; } = 1;

    public string ShardId { get; private set; } = null!;

    public bool RegexSearchEnabled { get; private set; }

    /// <summary>Optimistic concurrency token (ARCH-006).</summary>
    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public CloudSearchConfiguration ToDomain() => new(RegexSearchEnabled, new CloudAggregateVersion(Version));

    internal void ApplyScalars(CloudSearchConfiguration domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        RegexSearchEnabled = domain.RegexSearchEnabled;
        Version = domain.Version.Value;
    }
}
