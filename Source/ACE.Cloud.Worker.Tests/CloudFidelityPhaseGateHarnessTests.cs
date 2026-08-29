using System.Text.Json;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Cloud.Worker;

namespace ACE.Cloud.Worker.Tests;

/// <summary>
/// Issue #28's protected end-to-end fidelity gate: the operator-run harness that ties together the
/// icon corpus (<c>CloudIconCompositionGoldenTests</c>) and the appraisal capture corpus
/// (<c>CloudAppraisalGoldenCaptureComparisonTests</c>) issue #24/#25/#26/#27 already built and
/// explicitly deferred running for real to this issue, and emits the "machine-readable phase report
/// without embedding private art" the Green section asks for. Still reports Inconclusive rather than
/// failing when no corpus is configured on this machine -- ordinary CI never has an operator-owned DAT
/// or capture corpus, and never should.
///
/// To run this for real on a protected operator workstation, configure the same environment variables
/// the two underlying harnesses use (<c>ACE_CLOUD_MULE_DAT_DIRECTORY</c>,
/// <c>ACE_CLOUD_MULE_ICON_FIXTURE_DIRECTORY</c>, <c>ACE_CLOUD_MULE_APPRAISAL_CAPTURE_DIRECTORY</c>),
/// plus optionally <c>ACE_CLOUD_MULE_PHASE_GATE_REPORT_PATH</c> to also write the redacted JSON report
/// to disk. See <c>docs/agents/fidelity-phase-gate.md</c> for the full fixture contract.
/// </summary>
[TestClass]
public sealed class CloudFidelityPhaseGateHarnessTests
{
    [TestMethod]
    public async Task RunProtectedFidelityGate_OperatorOwnedCorpus_ProducesAPassingRedactedReport()
    {
        var datDirectory = Environment.GetEnvironmentVariable("ACE_CLOUD_MULE_DAT_DIRECTORY");
        var iconFixtureDirectory = Environment.GetEnvironmentVariable("ACE_CLOUD_MULE_ICON_FIXTURE_DIRECTORY");
        var appraisalCaptureDirectory = Environment.GetEnvironmentVariable("ACE_CLOUD_MULE_APPRAISAL_CAPTURE_DIRECTORY");

        var haveIconCorpus = !string.IsNullOrWhiteSpace(datDirectory) && !string.IsNullOrWhiteSpace(iconFixtureDirectory)
            && File.Exists(Path.Combine(datDirectory, "client_portal.dat"))
            && Directory.Exists(iconFixtureDirectory) && Directory.GetFiles(iconFixtureDirectory, "*.icon.json").Length > 0;

        var haveAppraisalCorpus = !string.IsNullOrWhiteSpace(appraisalCaptureDirectory)
            && Directory.Exists(appraisalCaptureDirectory) && Directory.GetFiles(appraisalCaptureDirectory, "*.appraisal.json").Length > 0;

        if (!haveIconCorpus && !haveAppraisalCorpus)
        {
            Assert.Inconclusive(
                "No local protected fidelity corpus is configured. This harness requires an operator-owned " +
                "workstation with ACE_CLOUD_MULE_DAT_DIRECTORY/ACE_CLOUD_MULE_ICON_FIXTURE_DIRECTORY and/or " +
                "ACE_CLOUD_MULE_APPRAISAL_CAPTURE_DIRECTORY configured -- see docs/agents/fidelity-phase-gate.md. " +
                "Ordinary CI never has this corpus and never should.");
            return;
        }

        var results = new List<CloudFidelityPhaseGateFixtureResult>();
        var nonBlockingGaps = new List<string>();
        string? storageRoot = null;

        try
        {
            if (haveIconCorpus)
            {
                storageRoot = Path.Combine(Path.GetTempPath(), "cloud-fidelity-phase-gate-tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(storageRoot);

                var blobStore = new LocalProtectedAssetBlobStore(new CloudAssetStorageOptions { RootDirectory = storageRoot });
                var manifestId = Guid.NewGuid();
                var sourcePath = Path.Combine(datDirectory!, "client_portal.dat");
                var entries = await new PortalDatAssetExtractor().ExtractAsync(sourcePath, manifestId, blobStore);

                var relativePathsByKey = entries.ToDictionary(
                    e => e.Key, e => CloudAssetStagingPathPolicy.BuildManifestEntryRelativePath(manifestId, e.Key));
                var blobReader = new CloudAssetManifestBlobReader(relativePathsByKey, blobStore);

                var iconFixtures = CloudGoldenFixtureLoader.LoadFromDirectory<CloudIconGoldenFixture>(iconFixtureDirectory!, "*.icon.json");
                var iconResults = await CloudIconGoldenComparisonHarness.CompareAsync(
                    iconFixtures, manifestVersion: 1, new PortalDatIconClothingEffectResolver(blobReader), new PortalDatIconLayerSource(blobReader));
                results.AddRange(iconResults);
            }
            else
            {
                nonBlockingGaps.Add("No icon fixture corpus configured on this run.");
            }

            if (haveAppraisalCorpus)
            {
                var appraisalFixtures = CloudGoldenFixtureLoader.LoadFromDirectory<CloudAppraisalGoldenFixture>(appraisalCaptureDirectory!, "*.appraisal.json");
                var appraisalReport = CloudAppraisalGoldenComparisonHarness.Compare(appraisalFixtures);
                results.AddRange(appraisalReport.Results.Select(r => new CloudFidelityPhaseGateFixtureResult
                {
                    Category = "Appraisal",
                    FixtureName = r.FixtureName,
                    Matched = r.Outcome == CloudAppraisalGoldenComparisonOutcome.Match,
                    Differences = r.Differences,
                }));
            }
            else
            {
                nonBlockingGaps.Add("No appraisal capture corpus configured on this run.");
            }

            var report = CloudFidelityPhaseGateReport.Combine(results, nonBlockingGaps);

            var reportPath = Environment.GetEnvironmentVariable("ACE_CLOUD_MULE_PHASE_GATE_REPORT_PATH");
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            }

            var failures = report.Results.Where(r => !r.Matched)
                .Select(r => $"[{r.Category}] {r.FixtureName}: {string.Join("; ", r.Differences)}")
                .ToList();

            Assert.IsTrue(report.AllPassed, "One or more fidelity fixtures mismatched:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
        }
        finally
        {
            if (storageRoot is not null && Directory.Exists(storageRoot))
            {
                Directory.Delete(storageRoot, recursive: true);
            }
        }
    }
}
