namespace ACE.Cloud.Persistence;

/// <summary>
/// One administrator-added custom Custodian Location (DEP-007), persisted independently of the
/// singleton <see cref="CloudCustodianConfigurationRecord"/> row it belongs to.
/// </summary>
public sealed class CloudCustodianCustomPositionRecord
{
    private CloudCustodianCustomPositionRecord()
    {
    }

    public CloudCustodianCustomPositionRecord(Guid id, string shardId, string positionRaw)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("A custom Custodian position requires a real ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A custom Custodian position requires a Cloud Shard ID.", nameof(shardId));
        }

        if (string.IsNullOrWhiteSpace(positionRaw))
        {
            throw new ArgumentException("A custom Custodian position requires its raw ACE position string.", nameof(positionRaw));
        }

        Id = id;
        ShardId = shardId;
        PositionRaw = positionRaw;
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public string PositionRaw { get; private set; } = null!;

    public DateTime CreatedAtUtc { get; private set; }
}
