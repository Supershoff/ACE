namespace ACE.Cloud.Persistence;

/// <summary>
/// The exclusive first-class record that keeps one native ACE biota out of world possession
/// (ARCH-005, INV-001). It is exclusively either a non-stack record (exactly one native biota,
/// exactly one Cloud owner, <see cref="OwnerId"/> set) or a stack record backing one or more
/// <see cref="CloudStackLot"/> rows (<see cref="TotalQuantity"/> set, no single owner), matching
/// CONTEXT.md's Cloud Custody Record entry: "identifies either its single Cloud owner or the
/// quantity lots backed by a stackable biota." A database CHECK constraint
/// (CK_CloudCustodyRecord_OwnerXorStack) enforces that exclusivity independently of application
/// code. See docs/adr/0002-defer-native-materialization-for-partial-stacks.md for why the backing
/// biota is never split until a world-boundary operation actually requires it.
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

    /// <summary>
    /// Creates a stack custody record: a stackable native biota held in Cloud custody with no
    /// single owner, whose <see cref="TotalQuantity"/> is exactly partitioned across one or more
    /// <see cref="CloudStackLot"/> rows (ARCH-010, ARCH-011, INV-001).
    /// </summary>
    public static CloudCustodyRecord CreateStack(uint biotaId, string shardId, int totalQuantity, Guid ledgerCorrelationId)
    {
        if (biotaId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(biotaId), "A Cloud Custody Record requires a real native biota GUID.");
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A Cloud Custody Record requires a Cloud Shard ID.", nameof(shardId));
        }

        if (totalQuantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalQuantity), "A stack Cloud Custody Record requires a positive total quantity.");
        }

        if (ledgerCorrelationId == Guid.Empty)
        {
            throw new ArgumentException(
                "A Cloud Custody Record requires an Activity Ledger correlation ID.", nameof(ledgerCorrelationId));
        }

        return new CloudCustodyRecord
        {
            Id = Guid.NewGuid(),
            BiotaId = biotaId,
            ShardId = shardId,
            OwnerId = null,
            TotalQuantity = totalQuantity,
            LedgerCorrelationId = ledgerCorrelationId,
            Version = 1,
        };
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// The exclusive native ACE biota GUID this record keeps out of world possession (INV-001).
    /// </summary>
    public uint BiotaId { get; private set; }

    public string ShardId { get; private set; } = null!;

    /// <summary>
    /// The opaque Cloud ownership identity that exclusively owns this non-stack custody record;
    /// null for a stack record, whose ownership is instead decomposed across its lots.
    /// </summary>
    public Guid? OwnerId { get; private set; }

    /// <summary>
    /// The backing stackable biota's total quantity, exactly summed by this record's
    /// <see cref="CloudStackLot"/> rows at all times; null for a non-stack record.
    /// </summary>
    public int? TotalQuantity { get; private set; }

    /// <summary>
    /// True when this record backs one or more Cloud Stack Lots rather than a single owner.
    /// </summary>
    public bool IsStack => TotalQuantity.HasValue;

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

    /// <summary>
    /// Shrinks a stack record's total quantity by exactly the amount a materializing withdrawal
    /// removed from Cloud custody (ARCH-010), keeping it equal to the sum of the surviving lots.
    /// Callers must hold this record's row lock and update it within the same transaction that
    /// mutates the affected <see cref="CloudStackLot"/> row(s).
    /// </summary>
    internal void ReduceStackTotalQuantity(int amount)
    {
        if (!IsStack)
        {
            throw new InvalidOperationException("Only a stack Cloud Custody Record's total quantity can be reduced.");
        }

        if (amount <= 0 || amount >= TotalQuantity!.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "A stack total reduction must be positive and leave a positive remainder.");
        }

        TotalQuantity -= amount;
        Version++;
    }

    /// <summary>
    /// Reassigns a non-stack record to a new owner without changing anything else (the "immediate
    /// cloud transfer" edge <see cref="CloudOwnershipTransferPolicy"/> validates, for example an
    /// Allegiance Vault contribution/take or Vault Absorption). Never valid for a stack record, whose
    /// ownership is decomposed across its <see cref="CloudStackLot"/> rows instead
    /// (<see cref="CloudStackLot.ChangeOwner"/>).
    /// </summary>
    internal void ChangeOwner(Guid newOwnerId)
    {
        if (IsStack)
        {
            throw new InvalidOperationException("A stack Cloud Custody Record has no single owner to reassign; change its lots' owners instead.");
        }

        if (newOwnerId == Guid.Empty)
        {
            throw new ArgumentException("A Cloud Custody Record requires exactly one owner.", nameof(newOwnerId));
        }

        OwnerId = newOwnerId;
        Version++;
    }
}
