using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single persisted row holding this deployment's shard-wide Storage Quota limits (INV-004). One
/// row per deployment (ARCH-001).
/// </summary>
public sealed class CloudStorageQuotaLimitsRecord
{
    private CloudStorageQuotaLimitsRecord()
    {
    }

    public static CloudStorageQuotaLimitsRecord CreateDefault(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Storage Quota limits row requires a Cloud Shard ID.", nameof(shardId));
        }

        return new CloudStorageQuotaLimitsRecord
        {
            Id = 1,
            ShardId = shardId,
            PersonalLimit = null,
            VaultLimit = null,
            Version = 1,
        };
    }

    public int Id { get; private set; } = 1;

    public string ShardId { get; private set; } = null!;

    public int? PersonalLimit { get; private set; }

    public int? VaultLimit { get; private set; }

    /// <summary>Optimistic concurrency token (ARCH-006).</summary>
    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public CloudStorageQuotaLimits ToDomain() => new(PersonalLimit, VaultLimit, new CloudAggregateVersion(Version));

    internal void ApplyScalars(CloudStorageQuotaLimits domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        PersonalLimit = domain.PersonalLimit;
        VaultLimit = domain.VaultLimit;
        Version = domain.Version.Value;
    }
}
