using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The single persisted row that binds this Cloud Mule deployment to its one immutable Cloud
/// Shard ID (ARCH-001) and records the schema/protocol versions it was applied with. The
/// database schema (see <see cref="CloudDbContext"/>) enforces that at most one row can exist.
/// </summary>
public sealed class CloudShardBinding
{
    private CloudShardBinding()
    {
    }

    public CloudShardBinding(
        string shardId,
        string schemaVersion,
        string aceExtensionVersion,
        string contractProtocolVersion)
    {
        ShardId = new CloudShardId(shardId).Value;
        SchemaVersion = schemaVersion;
        AceExtensionVersion = aceExtensionVersion;
        ContractProtocolVersion = contractProtocolVersion;
    }

    public int Id { get; private set; } = 1;

    public string ShardId { get; private set; } = null!;

    public string SchemaVersion { get; private set; } = null!;

    public string AceExtensionVersion { get; private set; } = null!;

    public string ContractProtocolVersion { get; private set; } = null!;

    public DateTime AppliedAtUtc { get; private set; }
}
