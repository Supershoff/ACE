namespace ACE.Cloud.Domain;

/// <summary>
/// An explicit typography/layout token for one <see cref="CloudAppraisalLine"/> (Green: "Represent
/// typography/layout semantics as explicit tokens rather than server-rendered HTML"). The later React
/// renderer maps each token to the AC-authentic ID panel's actual font weight/color; this projection
/// never emits markup or raw color values itself.
/// </summary>
public enum CloudAppraisalTextStyle
{
    Title,
    Body,
    Muted,

    /// <summary>A stat currently buffed by an active enchantment (green).</summary>
    Positive,

    /// <summary>A stat currently debuffed by an active enchantment, or a harmful spell (red).</summary>
    Negative,
}
