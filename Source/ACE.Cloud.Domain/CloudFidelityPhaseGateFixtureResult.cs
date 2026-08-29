namespace ACE.Cloud.Domain;

/// <summary>
/// One fixture's outcome inside a <see cref="CloudFidelityPhaseGateReport"/>. Deliberately carries no
/// file path, byte content, or other private-corpus detail (issue #28: "retain only redacted
/// machine-readable pass/fail metadata; never upload source art or private captures to GitHub") --
/// only the fixture's own declared name, its category, and human-readable diff strings that already
/// avoid embedding raw asset bytes (<see cref="CloudAppraisalGoldenComparisonHarness"/>'s diffs) or
/// DID-only diagnostic reasons.
/// </summary>
public sealed record CloudFidelityPhaseGateFixtureResult
{
    public required string Category { get; init; }

    public required string FixtureName { get; init; }

    public required bool Matched { get; init; }

    public IReadOnlyList<string> Differences { get; init; } = Array.Empty<string>();
}
