using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single persisted row holding this deployment's Global Cloud Maintenance state (ADM-004). One
/// row per deployment (ARCH-001), matching <c>CloudCustodianConfigurationRecord</c>'s established
/// singleton shape.
/// </summary>
public sealed class CloudGlobalMaintenanceRecord
{
    private CloudGlobalMaintenanceRecord()
    {
    }

    public static CloudGlobalMaintenanceRecord CreateDefault(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Global Cloud Maintenance row requires a Cloud Shard ID.", nameof(shardId));
        }

        return new CloudGlobalMaintenanceRecord
        {
            Id = 1,
            ShardId = shardId,
            IsFrozen = false,
            Reason = null,
            EnteredAtUtc = null,
            EnteredByAccountId = null,
            Version = 1,
        };
    }

    public int Id { get; private set; } = 1;

    public string ShardId { get; private set; } = null!;

    public bool IsFrozen { get; private set; }

    public string? Reason { get; private set; }

    public DateTime? EnteredAtUtc { get; private set; }

    public uint? EnteredByAccountId { get; private set; }

    /// <summary>Optimistic concurrency token (ARCH-006).</summary>
    public int Version { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public CloudGlobalMaintenanceState ToDomain() =>
        new(IsFrozen, Reason, EnteredAtUtc, EnteredByAccountId, new CloudAggregateVersion(Version));

    internal void ApplyScalars(CloudGlobalMaintenanceState domain)
    {
        ArgumentNullException.ThrowIfNull(domain);

        IsFrozen = domain.IsFrozen;
        Reason = domain.Reason;
        EnteredAtUtc = domain.EnteredAtUtc;
        EnteredByAccountId = domain.EnteredByAccountId;
        Version = domain.Version.Value;
    }
}
