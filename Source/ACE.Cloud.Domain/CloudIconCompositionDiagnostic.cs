namespace ACE.Cloud.Domain;

/// <summary>
/// One admin-visible reason a composition fell back instead of rendering (UI-006: "create an
/// administrator-visible diagnostic"). <see cref="DedupeKey"/> is the stable identity a durable store
/// upserts by, so a repeatedly-requested broken reference produces one growing diagnostic record
/// rather than one new row per render attempt. <see cref="DedupeKey"/> deliberately excludes
/// <see cref="ManifestVersion"/>: the same broken DID reference under a later manifest is still the
/// same underlying problem, not a new one. <see cref="ManifestVersion"/> is instead correlation
/// evidence (issue #28's Red requirement: "item/manifest correlation") so an administrator can tell
/// which Asset Manifest most recently reproduced a given diagnostic, e.g. after ASSET-002 activates a
/// reprocessed manifest.
/// </summary>
public sealed record CloudIconCompositionDiagnostic
{
    public CloudIconLayerReference Layer { get; }

    public CloudIconLayerResolutionOutcomeKind Reason { get; }

    public int ManifestVersion { get; }

    public CloudIconCompositionDiagnostic(CloudIconLayerReference layer, CloudIconLayerResolutionOutcomeKind reason, int manifestVersion)
    {
        if (reason == CloudIconLayerResolutionOutcomeKind.Resolved)
        {
            throw new ArgumentException("A diagnostic requires a non-Resolved reason.", nameof(reason));
        }

        if (manifestVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(manifestVersion));
        }

        Layer = layer;
        Reason = reason;
        ManifestVersion = manifestVersion;
    }

    public string DedupeKey => $"{Layer.Kind}:{Layer.Did:x8}:{Reason}";
}
