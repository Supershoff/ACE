using ACE.Cloud.Domain;
using ACE.Entity.Enum;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The rebuildable read-model cache of one native biota's category-relevant properties (UI-001,
/// issue #30 Green: "Implement category normalization ... in rebuildable projections"). Deliberately
/// separate from <see cref="CloudInventoryReadProjection"/> (which tracks "who currently owns this
/// biota" from the Custody Outbox) and from <see cref="CloudCustodyRecord"/>/<see cref="CloudStackLot"/>
/// (the authoritative identity/ownership/quantity source -- ARCH-005, ARCH-010): this row carries only
/// the static, rarely-changing facts (name, ItemType flags, WeenieType, value, burden, an icon
/// reference) a query needs to classify and display an item, so it can be safely dropped and
/// repopulated at any time from ACE's own biota properties without those authoritative tables ever
/// needing to change shape.
///
/// Populating this row from a live ACE <c>WorldObject</c>/biota is the responsibility of ACE's own
/// world-boundary code, exactly like <see cref="CloudAppraisalRawItemSnapshot"/>'s "producing this
/// snapshot from a live ACE WorldObject is the responsibility of ACE's own world-boundary code"
/// (ARCH-002): this project stays pure and never loads ACE.Server world objects, so which event
/// (deposit, a later backfill/reindex pass, ...) triggers <see cref="TryApply"/> is deliberately left
/// to that future integration seam rather than wired into this issue's transactional deposit boundary.
/// <see cref="Revision"/> gives that eventual writer the same idempotent, out-of-order-tolerant apply
/// guarantee <see cref="CloudProjectionSequenceGuard"/> already gives outbox-sourced projections,
/// without requiring this cache to share the Custody Outbox's own sequence numbering.
/// </summary>
public sealed class CloudInventoryItemPropertiesProjection
{
    private CloudInventoryItemPropertiesProjection()
    {
    }

    private CloudInventoryItemPropertiesProjection(uint biotaId, string shardId)
    {
        BiotaId = biotaId;
        ShardId = shardId;
    }

    public uint BiotaId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    /// <summary>The raw ACE <c>PropertyInt.ItemType</c> flag bits (UI-001).</summary>
    public uint ItemTypeFlags { get; private set; }

    /// <summary>The raw ACE <c>WeenieType</c> ordinal value (UI-001's documented fallback).</summary>
    public int WeenieType { get; private set; }

    /// <summary>
    /// Denormalized at write time via <see cref="CloudInventoryCategoryClassifier"/>, so a query can
    /// filter/sort by category directly in the database rather than re-classifying every row on
    /// every read.
    /// </summary>
    public CloudInventoryCategory Category { get; private set; }

    public int? Value { get; private set; }

    public int? Burden { get; private set; }

    public string? IconCacheKeyHex { get; private set; }

    /// <summary>Caller-supplied monotonic write guard (see this type's doc comment); 0 means never written.</summary>
    public long Revision { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Applies one property snapshot to a (possibly brand-new) row, following the same idempotent,
    /// order-tolerant rule <see cref="CloudProjectionSequenceGuard"/> already gives outbox-sourced
    /// projections. Returns the resulting row and whether anything was actually applied, so a caller
    /// can distinguish a genuine update from a stale/duplicate write that must be ignored.
    /// </summary>
    public static (CloudInventoryItemPropertiesProjection Row, bool Applied) TryApply(
        CloudInventoryItemPropertiesProjection? current,
        uint biotaId,
        string shardId,
        string name,
        ItemType itemType,
        WeenieType weenieType,
        int? value,
        int? burden,
        string? iconCacheKeyHex,
        long revision)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A properties row requires a Cloud Shard ID.", nameof(shardId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A properties row requires an item name.", nameof(name));
        }

        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "A properties row requires a positive revision.");
        }

        var row = current ?? new CloudInventoryItemPropertiesProjection(biotaId, shardId);

        if (!CloudProjectionSequenceGuard.ShouldApply(current?.Revision, revision))
        {
            return (row, Applied: false);
        }

        row.Name = name;
        row.ItemTypeFlags = (uint)itemType;
        row.WeenieType = (int)weenieType;
        row.Category = CloudInventoryCategoryClassifier.Classify(itemType, weenieType);
        row.Value = value;
        row.Burden = burden;
        row.IconCacheKeyHex = iconCacheKeyHex;
        row.Revision = revision;
        return (row, Applied: true);
    }
}
