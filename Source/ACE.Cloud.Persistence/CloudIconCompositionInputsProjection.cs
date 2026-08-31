using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The rebuildable read-model cache of one native biota's <see cref="CloudIconCompositionInputs"/>
/// (issue #34 human-acceptance correction: "Persist those rebuildable projections in ace_cloud" so a
/// runtime icon-composition worker -- which never has direct ace_shard access, ARCH-002/ARCH-004 --
/// can compose a missing/stale icon without ACE.Server needing to reference
/// <c>ACE.Cloud.Worker</c>'s DAT-reading types). Like <see cref="CloudInventoryItemPropertiesProjection"/>,
/// this row is fully disposable and rebuildable from ACE's own biota properties at any time.
/// </summary>
public sealed class CloudIconCompositionInputsProjection
{
    private CloudIconCompositionInputsProjection()
    {
    }

    private CloudIconCompositionInputsProjection(uint biotaId, string shardId)
    {
        BiotaId = biotaId;
        ShardId = shardId;
    }

    public uint BiotaId { get; private set; }

    public string ShardId { get; private set; } = null!;

    public uint? BaseIconDid { get; private set; }

    public uint? ClothingBaseDid { get; private set; }

    public uint SetupTableId { get; private set; }

    public int? PaletteTemplate { get; private set; }

    public float? Shade { get; private set; }

    public bool IgnoreCloIcons { get; private set; }

    public uint? UnderlayDid { get; private set; }

    public uint? OverlayDid { get; private set; }

    public uint? OverlaySecondaryDid { get; private set; }

    /// <summary>The resolved shared background DID for the item's ItemType, if the operator has one configured.</summary>
    public uint? ItemTypeBackgroundDid { get; private set; }

    /// <summary>Still magical/imbue-glow overlay DIDs, in draw order. Empty when the item has none active.</summary>
    public IReadOnlyList<uint> UiEffectDids { get; private set; } = Array.Empty<uint>();

    /// <summary>Caller-supplied monotonic write guard (see this type's doc comment); 0 means never written.</summary>
    public long Revision { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Applies one composition-inputs snapshot to a (possibly brand-new) row, following the same
    /// idempotent, order-tolerant rule <see cref="CloudProjectionSequenceGuard"/> already gives
    /// outbox-sourced projections.
    /// </summary>
    public static (CloudIconCompositionInputsProjection Row, bool Applied) TryApply(
        CloudIconCompositionInputsProjection? current,
        uint biotaId,
        string shardId,
        CloudIconCompositionInputs inputs,
        long revision)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An icon composition inputs row requires a Cloud Shard ID.", nameof(shardId));
        }

        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision), "An icon composition inputs row requires a positive revision.");
        }

        var row = current ?? new CloudIconCompositionInputsProjection(biotaId, shardId);

        if (!CloudProjectionSequenceGuard.ShouldApply(current?.Revision, revision))
        {
            return (row, Applied: false);
        }

        row.BaseIconDid = inputs.BaseIconDid;
        row.ClothingBaseDid = inputs.ClothingBaseDid;
        row.SetupTableId = inputs.SetupTableId;
        row.PaletteTemplate = inputs.PaletteTemplate;
        row.Shade = inputs.Shade;
        row.IgnoreCloIcons = inputs.IgnoreCloIcons;
        row.UnderlayDid = inputs.UnderlayDid;
        row.OverlayDid = inputs.OverlayDid;
        row.OverlaySecondaryDid = inputs.OverlaySecondaryDid;
        row.ItemTypeBackgroundDid = inputs.ItemTypeBackgroundDid;
        row.UiEffectDids = inputs.UiEffectDids ?? Array.Empty<uint>();
        row.Revision = revision;
        row.UpdatedAtUtc = DateTime.UtcNow;
        return (row, Applied: true);
    }

    /// <summary>
    /// Reconstructs the <see cref="CloudIconCompositionInputs"/> this row was written from, including
    /// the ACE-world-boundary-resolved <see cref="CloudIconCompositionInputs.ItemTypeBackgroundDid"/>
    /// and <see cref="CloudIconCompositionInputs.UiEffectDids"/> (issue #34 human-acceptance
    /// correction: both are now captured and persisted like every other field here, not left for a
    /// caller that has no route back to the operator's configured mapping).
    /// </summary>
    public CloudIconCompositionInputs ToInputs() => new()
    {
        BaseIconDid = BaseIconDid,
        ClothingBaseDid = ClothingBaseDid,
        SetupTableId = SetupTableId,
        PaletteTemplate = PaletteTemplate,
        Shade = Shade,
        IgnoreCloIcons = IgnoreCloIcons,
        UnderlayDid = UnderlayDid,
        OverlayDid = OverlayDid,
        OverlaySecondaryDid = OverlaySecondaryDid,
        ItemTypeBackgroundDid = ItemTypeBackgroundDid,
        UiEffectDids = UiEffectDids,
    };
}
