using System.Text.Json;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// The protected golden harness for Full Cloud Appraisal (UI-004), following the exact pattern
/// <c>CloudIconCompositionGoldenTests</c> established for issue #24/#25/#26: this requires an
/// operator-owned corpus of real ACE appraisal captures that is never committed to the repository,
/// so it reports Inconclusive rather than failing when no corpus is configured (Red: "do not require
/// private captures to merge this implementation issue"). Executing this harness for real against a
/// curated fidelity corpus (every relevant item class, wording, colors/flags, spells, requirements,
/// values, and special cases) is explicitly deferred to the #28 human gate; this issue's job is only
/// to prove the harness runs end to end and to document the fixture contract for #28 to extend.
///
/// To run this for real, set <c>ACE_CLOUD_MULE_APPRAISAL_CAPTURE_DIRECTORY</c> to a local directory
/// containing one or more <c>*.appraisal.json</c> files, each the JSON serialization of a
/// <see cref="CloudAppraisalGoldenFixture"/> (a raw item property capture paired with the exact panel
/// a real successful ACE appraisal is verified to produce). No DAT file, extracted client art, private
/// capture, secret, or absolute operator path may ever be committed or posted publicly.
/// </summary>
[TestClass]
public sealed class CloudAppraisalGoldenCaptureComparisonTests
{
    [TestMethod]
    public void Compare_OperatorOwnedCaptureCorpus_EveryFixtureMatches()
    {
        var captureDirectory = Environment.GetEnvironmentVariable("ACE_CLOUD_MULE_APPRAISAL_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(captureDirectory))
        {
            Assert.Inconclusive(
                "No local appraisal capture corpus is configured. Set ACE_CLOUD_MULE_APPRAISAL_CAPTURE_DIRECTORY to a " +
                "directory of *.appraisal.json CloudAppraisalGoldenFixture files to run this golden test. Executing " +
                "the full curated fidelity corpus is owned by issue #28.");
            return;
        }

        if (!Directory.Exists(captureDirectory))
        {
            Assert.Inconclusive($"ACE_CLOUD_MULE_APPRAISAL_CAPTURE_DIRECTORY is set, but {captureDirectory} does not exist.");
            return;
        }

        var captureFiles = Directory.GetFiles(captureDirectory, "*.appraisal.json", SearchOption.TopDirectoryOnly);
        if (captureFiles.Length == 0)
        {
            Assert.Inconclusive($"No *.appraisal.json fixture files were found under {captureDirectory}.");
            return;
        }

        var fixtures = captureFiles
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => JsonSerializer.Deserialize<CloudAppraisalGoldenFixture>(File.ReadAllText(path))
                ?? throw new InvalidDataException($"{path} did not deserialize to a CloudAppraisalGoldenFixture."))
            .ToList();

        var report = CloudAppraisalGoldenComparisonHarness.Compare(fixtures);

        var failures = report.Results
            .Where(r => r.Outcome == CloudAppraisalGoldenComparisonOutcome.Mismatch)
            .Select(r => $"{r.FixtureName}:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", r.Differences)}");

        Assert.IsTrue(report.AllMatch, "One or more appraisal captures mismatched:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }
}
