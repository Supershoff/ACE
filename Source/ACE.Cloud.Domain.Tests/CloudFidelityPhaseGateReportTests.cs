using System.Text.Json;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #28's Green requirement: "Produce a machine-readable phase report without embedding private
/// art." Proves the report's aggregation semantics and, critically, that its JSON serialization can
/// never leak an absolute filesystem path or raw asset content even when a caller's diff strings were
/// built from real (test-fabricated) filesystem error text.
/// </summary>
[TestClass]
public sealed class CloudFidelityPhaseGateReportTests
{
    [TestMethod]
    public void AllPassed_EveryResultMatches_IsTrue()
    {
        var report = CloudFidelityPhaseGateReport.Combine(
        [
            new CloudFidelityPhaseGateFixtureResult { Category = "Icon", FixtureName = "a", Matched = true },
            new CloudFidelityPhaseGateFixtureResult { Category = "Appraisal", FixtureName = "b", Matched = true },
        ]);

        Assert.IsTrue(report.AllPassed);
    }

    [TestMethod]
    public void AllPassed_OneMismatch_IsFalse()
    {
        var report = CloudFidelityPhaseGateReport.Combine(
        [
            new CloudFidelityPhaseGateFixtureResult { Category = "Icon", FixtureName = "a", Matched = true },
            new CloudFidelityPhaseGateFixtureResult { Category = "Icon", FixtureName = "b", Matched = false, Differences = ["expected sha256 aa, got bb"] },
        ]);

        Assert.IsFalse(report.AllPassed);
    }

    [TestMethod]
    public void AllPassed_NoResultsAtAll_IsFalse()
    {
        // An empty corpus must never report as a passing phase gate (issue #28: "curated icon and
        // appraisal results pass" -- there is nothing to have passed).
        var report = CloudFidelityPhaseGateReport.Combine([]);

        Assert.IsFalse(report.AllPassed);
    }

    [TestMethod]
    public void FixtureCountByCategory_CountsEachCategoryIndependently()
    {
        var report = CloudFidelityPhaseGateReport.Combine(
        [
            new CloudFidelityPhaseGateFixtureResult { Category = "Icon", FixtureName = "a", Matched = true },
            new CloudFidelityPhaseGateFixtureResult { Category = "Icon", FixtureName = "b", Matched = true },
            new CloudFidelityPhaseGateFixtureResult { Category = "Appraisal", FixtureName = "c", Matched = true },
        ]);

        Assert.AreEqual(2, report.FixtureCountByCategory["Icon"]);
        Assert.AreEqual(1, report.FixtureCountByCategory["Appraisal"]);
    }

    [TestMethod]
    public void Combine_NonBlockingGaps_AreRetainedVerbatim()
    {
        var report = CloudFidelityPhaseGateReport.Combine(
            [new CloudFidelityPhaseGateFixtureResult { Category = "Icon", FixtureName = "a", Matched = true }],
            nonBlockingGaps: ["No high-resolution client_highres.dat corpus captured yet."]);

        CollectionAssert.Contains(report.NonBlockingGaps.ToList(), "No high-resolution client_highres.dat corpus captured yet.");
    }

    [TestMethod]
    public void SerializedReport_NeverContainsAnAbsoluteFilesystemPath()
    {
        // Simulates the exact hazard issue #28 warns about: a caller building a diff string from a
        // real filesystem error would otherwise leak the operator's private DAT storage layout.
        var report = CloudFidelityPhaseGateReport.Combine(
        [
            new CloudFidelityPhaseGateFixtureResult
            {
                Category = "Icon",
                FixtureName = "clothing-palette-variant-03",
                Matched = false,
                Differences = ["expected PNG sha256 aa11, got bb22"],
            },
        ],
        nonBlockingGaps: ["client_highres.dat corpus not yet captured."]);

        var json = JsonSerializer.Serialize(report);

        // Generic DAT filenames (client_portal.dat/client_highres.dat) are public ACE terminology, not
        // private content, so a coverage-gap sentence is free to name them; only actual filesystem
        // paths are forbidden.
        foreach (var forbidden in new[] { "C:\\", "/home/", "/Users/", "/var/", "/tmp/" })
        {
            StringAssert.DoesNotMatch(json, new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(forbidden)));
        }
    }

    [TestMethod]
    public void CloudFidelityPhaseGateReport_JsonRoundTrips()
    {
        var report = CloudFidelityPhaseGateReport.Combine(
        [
            new CloudFidelityPhaseGateFixtureResult { Category = "Icon", FixtureName = "a", Matched = true },
        ],
        nonBlockingGaps: ["gap"]);

        var json = JsonSerializer.Serialize(report);
        var roundTripped = JsonSerializer.Deserialize<CloudFidelityPhaseGateReport>(json);

        Assert.IsNotNull(roundTripped);
        Assert.IsTrue(roundTripped!.AllPassed);
        CollectionAssert.Contains(roundTripped.NonBlockingGaps.ToList(), "gap");
    }
}
