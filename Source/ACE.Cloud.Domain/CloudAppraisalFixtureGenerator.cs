using System.Text.Json;

namespace ACE.Cloud.Domain;

/// <summary>
/// Issue #28's local-only appraisal fixture-generation tooling (Green: "prepares <c>*.appraisal.json</c>
/// from operator-owned successful ACE appraisal captures"). An operator supplies only the raw item
/// property capture they took from their own successful ACE appraisal (a <see cref="CloudAppraisalRawItemSnapshot"/>)
/// and a fixture name; this type derives the expected panel by running the exact same deterministic
/// <see cref="CloudAppraisalProjector"/> the harness later re-runs to verify against, so the operator
/// never hand-authors the nested <see cref="CloudAppraisalPanel"/>/section/line JSON the fixture
/// contract otherwise requires (issue #28: "the operator must not have to hand-author the fixture
/// contracts"). Because the projector is pure, this is not circular: it freezes today's confirmed-correct
/// projection as a regression baseline for every future change to <see cref="CloudAppraisalProjector"/>,
/// exactly like any other golden/snapshot test.
/// </summary>
public static class CloudAppraisalFixtureGenerator
{
    public static CloudAppraisalGoldenFixture Generate(string fixtureName, CloudAppraisalRawItemSnapshot capturedSnapshot)
    {
        CloudFixtureContractSanitizer.ValidateFixtureName(fixtureName, nameof(fixtureName));
        ArgumentNullException.ThrowIfNull(capturedSnapshot);

        var fixture = new CloudAppraisalGoldenFixture
        {
            FixtureName = fixtureName,
            Snapshot = capturedSnapshot,
            ExpectedPanel = CloudAppraisalProjector.Build(capturedSnapshot),
        };

        CloudFixtureContractSanitizer.EnsureNoAbsolutePath(JsonSerializer.Serialize(fixture), fixtureName);
        return fixture;
    }

    /// <summary>Generates a fixture from an operator-owned capture and writes it as <c>{fixtureName}.appraisal.json</c> under <paramref name="outputDirectory"/>.</summary>
    public static async Task<string> GenerateAndWriteAsync(
        string fixtureName, CloudAppraisalRawItemSnapshot capturedSnapshot, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var fixture = Generate(fixtureName, capturedSnapshot);

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        }

        var json = JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true });
        CloudFixtureContractSanitizer.EnsureNoAbsolutePath(json, fixture.FixtureName);

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{fixture.FixtureName}.appraisal.json");
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
        return outputPath;
    }
}
