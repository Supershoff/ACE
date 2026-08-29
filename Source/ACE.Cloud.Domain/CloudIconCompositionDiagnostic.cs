namespace ACE.Cloud.Domain;

/// <summary>
/// One admin-visible reason a composition fell back instead of rendering (UI-006: "create an
/// administrator-visible diagnostic"). <see cref="DedupeKey"/> is the stable identity a durable store
/// upserts by, so a repeatedly-requested broken reference produces one growing diagnostic record
/// rather than one new row per render attempt.
/// </summary>
public sealed record CloudIconCompositionDiagnostic
{
    public CloudIconLayerReference Layer { get; }

    public CloudIconLayerResolutionOutcomeKind Reason { get; }

    public CloudIconCompositionDiagnostic(CloudIconLayerReference layer, CloudIconLayerResolutionOutcomeKind reason)
    {
        if (reason == CloudIconLayerResolutionOutcomeKind.Resolved)
        {
            throw new ArgumentException("A diagnostic requires a non-Resolved reason.", nameof(reason));
        }

        Layer = layer;
        Reason = reason;
    }

    public string DedupeKey => $"{Layer.Kind}:{Layer.Did:x8}:{Reason}";
}
