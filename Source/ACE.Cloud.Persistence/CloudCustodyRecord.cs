namespace ACE.Cloud.Persistence;

/// <summary>
/// The exclusive first-class record that keeps one native ACE biota out of world possession
/// (ARCH-005, INV-001). This issue proves non-stack custody only: exactly one native biota and
/// exactly one Cloud owner. A later issue adds the CloudStackLot join described in
/// docs/adr/0002-defer-native-materialization-for-partial-stacks.md for stackable biotas.
/// </summary>
public sealed class CloudCustodyRecord
{
    private CloudCustodyRecord()
    {
    }

    public CloudCustodyRecord(uint biotaId, string shardId, Guid ownerId, Guid ledgerCorrelationId)
    {
        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "A Cloud Custody Record requires a real native biota GUID.");
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Cloud Custody Record requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Custody Record requires exactly one owner.", nameof(ownerId));
        }

        if (ledgerCorrelationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A Cloud Custody Record requires an Activity Ledger correlation ID.", nameof(ledgerCorrelationId));
        }

        Id = Guid.NewGuid();
        BiotaId = biotaId;
        ShardId = shardId;
        OwnerId = ownerId;
        LedgerCorrelationId = ledgerCorrelationId;
        Version = 1;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The exclusive native ACE biota GUID this record keeps out of world possession (INV-001).
    /// </summary>
    public uint BiotaId { get; private set; }

    public string ShardId { get; private set; } = null!;

    /// <summary>
    /// The opaque Cloud ownership identity that exclusively owns this non-stack custody record.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// The Activity Ledger correlation ID for the boundary transaction that created this record
    /// (EVT-002); a later issue expands this into the full Activity Ledger.
    /// </summary>
    public Guid LedgerCorrelationId { get; private set; }

    /// <summary>
    /// Optimistic concurrency token (ARCH-006).
    /// </summary>
    public int Version { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }
}
