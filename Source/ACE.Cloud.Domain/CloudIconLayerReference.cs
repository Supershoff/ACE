namespace ACE.Cloud.Domain;

/// <summary>
/// One layer a <see cref="CloudIconLayerPlan"/> asks the compositor to resolve and draw (UI-005).
/// Unlike <see cref="CloudAssetManifestEntryKey"/>, <see cref="Did"/> 0 is a legal value here: it is
/// the explicit "no candidate DID exists for this required layer" sentinel (for example an item whose
/// ClothingBase has no effect for its Setup and which also has no plain <c>Icon</c> property), which
/// the compositor turns into a <see cref="CloudIconLayerResolutionOutcomeKind.Missing"/> diagnostic
/// without attempting a lookup.
/// </summary>
public readonly record struct CloudIconLayerReference
{
    public CloudIconLayerKind Kind { get; }

    public uint Did { get; }

    public CloudIconLayerReference(CloudIconLayerKind kind, uint did)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        Did = did;
    }

    public bool IsUnresolvable => Did == 0;
}
