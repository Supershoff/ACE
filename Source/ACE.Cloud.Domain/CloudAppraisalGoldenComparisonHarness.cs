namespace ACE.Cloud.Domain;

/// <summary>
/// The protected comparison harness for operator-owned item captures (Red: "Build a protected
/// comparison harness for operator-owned item captures, but do not require private captures to merge
/// this implementation issue"). This type itself is pure and requires no file/network access -- it
/// only projects each fixture's snapshot and diffs the result against the fixture's expected panel.
/// A caller that reads a curated corpus from disk (the #28 human gate's job) is expected to feed its
/// deserialized <see cref="CloudAppraisalGoldenFixture"/> instances straight into <see cref="Compare"/>.
/// </summary>
public static class CloudAppraisalGoldenComparisonHarness
{
    public static CloudAppraisalGoldenComparisonReport Compare(IReadOnlyList<CloudAppraisalGoldenFixture> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);

        var results = fixtures.Select(CompareOne).ToList();
        return new CloudAppraisalGoldenComparisonReport { Results = results };
    }

    private static CloudAppraisalGoldenComparisonResult CompareOne(CloudAppraisalGoldenFixture fixture)
    {
        var actual = CloudAppraisalProjector.Build(fixture.Snapshot);
        var differences = Diff(fixture.ExpectedPanel, actual).ToList();

        return new CloudAppraisalGoldenComparisonResult
        {
            FixtureName = fixture.FixtureName,
            Outcome = differences.Count == 0 ? CloudAppraisalGoldenComparisonOutcome.Match : CloudAppraisalGoldenComparisonOutcome.Mismatch,
            Differences = differences,
        };
    }

    private static IEnumerable<string> Diff(CloudAppraisalPanel expected, CloudAppraisalPanel actual)
    {
        if (expected.ContractVersion != actual.ContractVersion)
        {
            yield return $"ContractVersion: expected {expected.ContractVersion}, got {actual.ContractVersion}";
        }

        if (expected.ItemName != actual.ItemName)
        {
            yield return $"ItemName: expected '{expected.ItemName}', got '{actual.ItemName}'";
        }

        var maxSections = Math.Max(expected.Sections.Count, actual.Sections.Count);
        for (var sectionIndex = 0; sectionIndex < maxSections; sectionIndex++)
        {
            var expectedSection = sectionIndex < expected.Sections.Count ? expected.Sections[sectionIndex] : null;
            var actualSection = sectionIndex < actual.Sections.Count ? actual.Sections[sectionIndex] : null;

            if (expectedSection is null)
            {
                yield return $"Section[{sectionIndex}]: unexpected extra section '{actualSection!.Kind}'";
                continue;
            }

            if (actualSection is null)
            {
                yield return $"Section[{sectionIndex}]: expected section '{expectedSection.Kind}' is missing";
                continue;
            }

            if (expectedSection.Kind != actualSection.Kind)
            {
                yield return $"Section[{sectionIndex}].Kind: expected '{expectedSection.Kind}', got '{actualSection.Kind}'";
            }

            var maxLines = Math.Max(expectedSection.Lines.Count, actualSection.Lines.Count);
            for (var lineIndex = 0; lineIndex < maxLines; lineIndex++)
            {
                var expectedLine = lineIndex < expectedSection.Lines.Count ? expectedSection.Lines[lineIndex] : null;
                var actualLine = lineIndex < actualSection.Lines.Count ? actualSection.Lines[lineIndex] : null;

                if (expectedLine != actualLine)
                {
                    yield return $"Section[{sectionIndex}]({expectedSection.Kind}).Line[{lineIndex}]: expected {Describe(expectedLine)}, got {Describe(actualLine)}";
                }
            }
        }
    }

    private static string Describe(CloudAppraisalLine? line) =>
        line is null ? "<missing>" : $"'{line.Text}' ({line.Style})";
}
