using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The authorization-scoped read/search projection of one native biota's current Custody Outbox
/// state (ARCH-007, SRCH-001): "who currently owns this Cloud Item, and what was the last committed
/// custody operation." Built exclusively by replaying <see cref="CloudCustodyOutboxEvent"/> rows
/// through <see cref="TryApply"/> -- never by reading <see cref="CloudCustodyRecord"/> directly --
/// so this table proves the outbox itself is sufficient to reconstruct the read model, which is
/// exactly what "a clean rebuild produces the same query state as incremental consumption" requires.
/// Disposable by design (ARCH-012: "search uses a rebuildable indexed read model," not a second
/// mandatory authority database): MariaDB's <see cref="CloudCustodyRecord"/> remains the only
/// authoritative custody state, and this table may always be safely dropped and rebuilt from the
/// outbox alone.
/// </summary>
public sealed class CloudInventoryReadProjection
{
    private CloudInventoryReadProjection()
    {
    }

    private CloudInventoryReadProjection(uint biotaId, string shardId)
    {
        BiotaId = biotaId;
        ShardId = shardId;
    }

    public uint BiotaId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public Guid OwnerId { get; private set; }

    public CloudBoundaryOperationType LastEventType { get; private set; }

    /// <summary>The outbox <see cref="CloudCustodyOutboxEvent.SequenceNumber"/> this row last applied (ARCH-007).</summary>
    public long LastAppliedSequenceNumber { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Applies one Custody Outbox event to a (possibly brand-new) projection row, following
    /// <see cref="CloudProjectionSequenceGuard"/>'s idempotent, order-tolerant rule. Returns the
    /// resulting row -- either <paramref name="current"/> updated in place, a freshly created row, or
    /// <paramref name="current"/> unchanged when the event is a stale/duplicate delivery that must be
    /// ignored -- and whether anything was actually applied, so the caller knows whether to also
    /// publish a Live State Stream update (a duplicate/stale delivery must never re-publish).
    /// </summary>
    public static (CloudInventoryReadProjection Row, bool Applied) TryApply(
        CloudInventoryReadProjection? current,
        uint biotaId,
        string shardId,
        Guid ownerId,
        CloudBoundaryOperationType eventType,
        long sequenceNumber)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A projection row requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A projection row requires an owner.", nameof(ownerId));
        }

        var row = current ?? new CloudInventoryReadProjection(biotaId, shardId);

        if (!CloudProjectionSequenceGuard.ShouldApply(current?.LastAppliedSequenceNumber, sequenceNumber))
        {
            return (row, Applied: false);
        }

        row.OwnerId = ownerId;
        row.LastEventType = eventType;
        row.LastAppliedSequenceNumber = sequenceNumber;
        return (row, Applied: true);
    }
}
