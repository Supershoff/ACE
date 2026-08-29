namespace ACE.Cloud.Domain;

/// <summary>
/// Every instance property Icon Reconstruction composes from, and nothing else (UI-005, UI-006:
/// "Stack counts, selection, reservation, and other web state remain separate overlays and never
/// alter the reconstructed source icon"). Deliberately excludes quantity, selection, and reservation
/// state -- there is no field here they could occupy, so a caller cannot accidentally fold web-only
/// state into a composition key derived from this type. <see cref="ItemTypeBackgroundDid"/> is
/// supplied pre-resolved by the caller (issue #24: "Select the shared background DID from the item's
/// ItemType; it is not an item icon property") rather than computed here, because the ItemType-&gt;
/// background mapping is real-DAT-derived data this bounded issue does not own or fabricate.
/// </summary>
public sealed record CloudIconCompositionInputs
{
    /// <summary>The item's own <c>Icon</c> property (<c>PropertyDataId.Icon</c>), if set.</summary>
    public uint? BaseIconDid { get; init; }

    /// <summary>The item's <c>ClothingBase</c> property, if set (drives palette/shade + icon override).</summary>
    public uint? ClothingBaseDid { get; init; }

    /// <summary>The item's <c>Setup</c> DID low bits, used to select the applicable <c>ClothingBaseEffect</c>.</summary>
    public uint SetupTableId { get; init; }

    /// <summary>The item's <c>PaletteTemplate</c> property.</summary>
    public int? PaletteTemplate { get; init; }

    /// <summary>The item's <c>Shade</c> property.</summary>
    public float? Shade { get; init; }

    /// <summary>True when <c>IgnoreCloIcons</c> is set, suppressing a clothing icon override even if one resolves.</summary>
    public bool IgnoreCloIcons { get; init; }

    /// <summary>The item's <c>IconUnderlay</c> property, if set.</summary>
    public uint? UnderlayDid { get; init; }

    /// <summary>The item's <c>IconOverlay</c> property, if set.</summary>
    public uint? OverlayDid { get; init; }

    /// <summary>The item's <c>IconOverlaySecondary</c> property, if set.</summary>
    public uint? OverlaySecondaryDid { get; init; }

    /// <summary>The caller-resolved shared background DID for the item's <c>ItemType</c>, if any.</summary>
    public uint? ItemTypeBackgroundDid { get; init; }

    /// <summary>
    /// Still magical/imbue-glow overlay DIDs, drawn last, in this exact order (UI-006: "magical glow
    /// is a still blue layer"). Empty for an item with no active UiEffects.
    /// </summary>
    public IReadOnlyList<uint> UiEffectDids { get; init; } = Array.Empty<uint>();

    public CloudIconCompositionInputs()
    {
    }
}
