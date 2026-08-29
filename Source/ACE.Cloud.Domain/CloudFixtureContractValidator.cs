using System.Text.Json;

namespace ACE.Cloud.Domain;

/// <summary>
/// Issue #28's local-only validation half of the fixture-generation tooling ("A documented local-only
/// command generates/validates the icon and appraisal fixture contracts"). Re-checks an already-written
/// <c>*.icon.json</c>/<c>*.appraisal.json</c> file against the same rules <see cref="CloudIconFixtureGenerator"/>/
/// <see cref="CloudAppraisalFixtureGenerator"/> enforce at generation time, so a fixture an operator
/// hand-edited after generation (or received from someone else) is still caught before it is ever fed
/// into <see cref="CloudIconGoldenComparisonHarness"/>/<see cref="CloudAppraisalGoldenComparisonHarness"/>.
/// Returns every problem found rather than throwing on the first one, so an operator can fix a fixture in
/// one pass. An empty result means the fixture is valid.
/// </summary>
public static class CloudFixtureContractValidator
{
    public static IReadOnlyList<string> ValidateIconFixture(CloudIconGoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(fixture.FixtureName))
        {
            problems.Add("FixtureName is required.");
        }
        else if (fixture.FixtureName.IndexOfAny(['/', '\\']) >= 0 || fixture.FixtureName.Contains(".."))
        {
            problems.Add($"FixtureName '{fixture.FixtureName}' must be a plain name, not a path.");
        }

        if (fixture.ExpectedPngSha256Hex is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit))
        {
            problems.Add("ExpectedPngSha256Hex must be exactly 64 hexadecimal characters.");
        }

        AddIfAbsolutePath(problems, JsonSerializer.Serialize(fixture), "the fixture");

        return problems;
    }

    /// <summary>
    /// In addition to the structural checks above, re-derives the expected panel from the fixture's
    /// own snapshot and confirms it still matches -- catching a fixture whose ExpectedPanel was hand-edited
    /// (or drifted from a <see cref="CloudAppraisalProjector"/> change) after generation, since
    /// <see cref="CloudAppraisalFixtureGenerator"/> never expects an operator to author ExpectedPanel by hand.
    /// </summary>
    public static IReadOnlyList<string> ValidateAppraisalFixture(CloudAppraisalGoldenFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(fixture.FixtureName))
        {
            problems.Add("FixtureName is required.");
        }
        else if (fixture.FixtureName.IndexOfAny(['/', '\\']) >= 0 || fixture.FixtureName.Contains(".."))
        {
            problems.Add($"FixtureName '{fixture.FixtureName}' must be a plain name, not a path.");
        }

        var rederivedPanel = CloudAppraisalProjector.Build(fixture.Snapshot);
        if (!Equals(rederivedPanel, fixture.ExpectedPanel))
        {
            problems.Add(
                "ExpectedPanel does not match CloudAppraisalProjector.Build(Snapshot). Regenerate this fixture with " +
                "CloudAppraisalFixtureGenerator rather than hand-editing ExpectedPanel.");
        }

        AddIfAbsolutePath(problems, JsonSerializer.Serialize(fixture), "the fixture");

        return problems;
    }

    private static void AddIfAbsolutePath(List<string> problems, string serialized, string description)
    {
        if (CloudFixtureContractSanitizer.ContainsAbsolutePath(serialized))
        {
            problems.Add($"The serialized contract for {description} appears to embed an absolute filesystem path.");
        }
    }
}
