using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One deduplicated, administrator-visible Icon Reconstruction failure (UI-006: "create an
/// administrator-visible diagnostic"). Rows are identified by (<see cref="ShardId"/>,
/// <see cref="DedupeKey"/>) -- <see cref="CloudIconCompositionDiagnostic.DedupeKey"/> -- so a
/// repeatedly requested broken reference grows one row's <see cref="OccurrenceCount"/> instead of
/// producing one new row per render attempt.
/// </summary>
public sealed class CloudIconDiagnostic
{
    private CloudIconDiagnostic()
    {
    }

    public CloudIconDiagnostic(Guid id, string shardId, CloudIconCompositionDiagnostic diagnostic, DateTime nowUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("An icon diagnostic requires a real ID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An icon diagnostic requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(diagnostic);

        Id = id;
        ShardId = shardId;
        DedupeKey = diagnostic.DedupeKey;
        LayerKind = diagnostic.Layer.Kind;
        Did = diagnostic.Layer.Did;
        Reason = diagnostic.Reason;
        OccurrenceCount = 1;
        FirstSeenAtUtc = nowUtc;
        LastSeenAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }

    public string ShardId { get; private set; } = null!;

    public string DedupeKey { get; private set; } = null!;

    public CloudIconLayerKind LayerKind { get; private set; }

    public uint Did { get; private set; }

    public CloudIconLayerResolutionOutcomeKind Reason { get; private set; }

    public int OccurrenceCount { get; private set; }

    public DateTime FirstSeenAtUtc { get; private set; }

    public DateTime LastSeenAtUtc { get; private set; }
}
