namespace ACE.Cloud.Domain;

/// <summary>
/// One layer in an Icon Reconstruction composite, in the order issue #24 validated against the
/// TreeStats WCID 42635 reference item and CONTEXT.md's "Icon Reconstruction" definition confirms:
/// background -&gt; underlay -&gt; base icon -&gt; overlay -&gt; secondary overlay, with static UiEffects
/// (magical glow) drawn last, on top of everything else (UI-005, UI-006).
/// </summary>
public enum CloudIconLayerKind
{
    Background,
    Underlay,
    BaseIcon,
    Overlay,
    OverlaySecondary,
    UiEffect,
}
