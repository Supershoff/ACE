namespace ACE.Cloud.Domain;

/// <summary>
/// Issue #28's machine-readable phase-gate evidence: "Produce a machine-readable phase report without
/// embedding private art" and "A phase-gate report identifies fixture coverage and any explicit
/// non-blocking high-resolution gaps." One report always covers every fixture category the operator's
/// protected harnesses ran (Icon Reconstruction, Full Cloud Appraisal), never just one, so a partial
/// corpus cannot be reported as a complete pass. This type is pure and JSON-serializable with
/// <c>System.Text.Json</c>'s default reflection contract, exactly like <see cref="CloudAppraisalGoldenFixture"/>.
/// </summary>
public sealed record CloudFidelityPhaseGateReport
{
    /// <summary>
    /// The fixture categories this phase gate always requires (issue #28: "The protected phase gate
    /// must require non-empty Icon and Appraisal corpora... A missing required category is blocking,
    /// not a non-blocking gap."). A run that only ever exercised one of these -- an Icon-only or
    /// Appraisal-only corpus -- can never satisfy <see cref="AllPassed"/>, regardless of how many
    /// fixtures it included or how cleanly they all matched.
    /// </summary>
    public static readonly IReadOnlyList<string> RequiredCategories = ["Icon", "Appraisal"];

    public required IReadOnlyList<CloudFidelityPhaseGateFixtureResult> Results { get; init; }

    /// <summary>
    /// Explicit, named coverage gaps that do not block this phase gate (e.g. a curated corpus category
    /// an operator has not yet captured). Never silently omitted -- an empty list here is itself the
    /// claim "no known gaps," so callers must populate it deliberately. A missing <see cref="RequiredCategories"/>
    /// entry is never appropriate here -- it always belongs in <see cref="MissingRequiredCategories"/> instead,
    /// which blocks the gate rather than merely noting it.
    /// </summary>
    public IReadOnlyList<string> NonBlockingGaps { get; init; } = Array.Empty<string>();

    /// <summary>One count per category, e.g. {"Icon": 12, "Appraisal": 8}, for coverage reporting.</summary>
    public IReadOnlyDictionary<string, int> FixtureCountByCategory =>
        Results.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>
    /// Which of <see cref="RequiredCategories"/> have zero fixtures in this run. Always blocking: a
    /// non-empty list here forces <see cref="AllPassed"/> false even if every included fixture matched.
    /// </summary>
    public IReadOnlyList<string> MissingRequiredCategories =>
        RequiredCategories.Where(category => !FixtureCountByCategory.ContainsKey(category)).ToList();

    public bool AllPassed =>
        Results.Count > 0 && Results.All(r => r.Matched) && MissingRequiredCategories.Count == 0;

    public static CloudFidelityPhaseGateReport Combine(
        IEnumerable<CloudFidelityPhaseGateFixtureResult> results, IEnumerable<string>? nonBlockingGaps = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        return new CloudFidelityPhaseGateReport
        {
            Results = results.ToList(),
            NonBlockingGaps = nonBlockingGaps?.ToList() ?? new List<string>(),
        };
    }
}
