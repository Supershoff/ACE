namespace ACE.Cloud.Domain;

/// <summary>
/// Issue #39's Green requirement: "Produce a machine-readable phase coverage report mapped to
/// requirement IDs." Mirrors <see cref="CloudFidelityPhaseGateReport"/>'s established shape (issue
/// #28) exactly -- a required, named set of IDs that must each carry at least one piece of evidence,
/// with any gap blocking rather than silently passing. Unlike the fidelity report's fixture
/// categories, the required set here is per-requirement-ID (XFER-001, XFER-002, SHARE-001..004,
/// VAULT-001..005), matching the issue's own "Requirements" list exactly -- a report that only ever
/// covers, say, VAULT-004 and VAULT-005 while claiming the whole vault surface passed is exactly the
/// silent-partial-pass failure mode this type exists to prevent.
/// </summary>
public sealed record CloudCollaborationPhaseGateReport
{
    /// <summary>Issue #39's own "Requirements" list -- every ID this phase gate must carry at least one piece of evidence for.</summary>
    public static readonly IReadOnlyList<string> RequiredRequirementIds =
    [
        "XFER-001", "XFER-002",
        "SHARE-001", "SHARE-002", "SHARE-003", "SHARE-004",
        "VAULT-001", "VAULT-002", "VAULT-003", "VAULT-004", "VAULT-005",
    ];

    public required IReadOnlyList<CloudCollaborationPhaseGateEvidence> Evidence { get; init; }

    /// <summary>
    /// Explicit, named coverage gaps that do not block this phase gate (mirrors
    /// <see cref="CloudFidelityPhaseGateReport.NonBlockingGaps"/>'s own contract: never silently
    /// omitted, so an empty list here is itself the deliberate claim "no known non-blocking gaps").
    /// </summary>
    public IReadOnlyList<string> NonBlockingGaps { get; init; } = Array.Empty<string>();

    /// <summary>Every requirement ID with at least one piece of evidence, deduplicated.</summary>
    public IReadOnlyList<string> CoveredRequirementIds => Evidence
        .Select(e => e.RequirementId)
        .Distinct()
        .ToList();

    /// <summary>One evidence count per collaboration category, e.g. {"Offer": 6, "Sharing": 5, "Vault": 8}.</summary>
    public IReadOnlyDictionary<CloudCollaborationPhaseGateCategory, int> EvidenceCountByCategory =>
        Evidence.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Which of <see cref="RequiredRequirementIds"/> have zero evidence in this report. Always blocking.</summary>
    public IReadOnlyList<string> MissingRequirementIds
    {
        get
        {
            var covered = new HashSet<string>(CoveredRequirementIds, StringComparer.Ordinal);
            return RequiredRequirementIds.Where(id => !covered.Contains(id)).ToList();
        }
    }

    /// <summary>
    /// True only when every required requirement ID has at least one piece of evidence and every
    /// collaboration category (<see cref="CloudCollaborationPhaseGateCategory"/>) is represented --
    /// the same "a category with zero fixtures can never pass" rule
    /// <see cref="CloudFidelityPhaseGateReport.AllPassed"/> applies, adapted to requirement IDs.
    /// </summary>
    public bool AllPassed =>
        Evidence.Count > 0
        && MissingRequirementIds.Count == 0
        && Enum.GetValues<CloudCollaborationPhaseGateCategory>().All(category => EvidenceCountByCategory.ContainsKey(category));

    public static CloudCollaborationPhaseGateReport Combine(
        IEnumerable<CloudCollaborationPhaseGateEvidence> evidence, IEnumerable<string>? nonBlockingGaps = null)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return new CloudCollaborationPhaseGateReport
        {
            Evidence = evidence.ToList(),
            NonBlockingGaps = nonBlockingGaps?.ToList() ?? new List<string>(),
        };
    }
}
