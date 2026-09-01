using System.Text.Json;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #39's Green requirement: "Produce a machine-readable phase coverage report mapped to
/// requirement IDs." Mirrors <see cref="CloudFidelityPhaseGateReportTests"/>'s established shape
/// (issue #28) exactly, adapted from fixture categories to requirement IDs.
/// </summary>
[TestClass]
public sealed class CloudCollaborationPhaseGateReportTests
{
    private static CloudCollaborationPhaseGateEvidence Evidence(string requirementId, CloudCollaborationPhaseGateCategory category) =>
        new() { RequirementId = requirementId, Category = category, Evidence = $"Test.{requirementId}", Description = "test evidence" };

    [TestMethod]
    public void AllPassed_EveryRequiredRequirementIdCoveredAcrossAllCategories_IsTrue()
    {
        var evidence = CloudCollaborationPhaseGateReport.RequiredRequirementIds
            .Select(id => Evidence(id, id.StartsWith("XFER") ? CloudCollaborationPhaseGateCategory.Offer
                : id.StartsWith("SHARE") ? CloudCollaborationPhaseGateCategory.Sharing
                : CloudCollaborationPhaseGateCategory.Vault));

        var report = CloudCollaborationPhaseGateReport.Combine(evidence);

        Assert.IsTrue(report.AllPassed);
        Assert.HasCount(0, report.MissingRequirementIds);
    }

    [TestMethod]
    public void AllPassed_NoEvidenceAtAll_IsFalse()
    {
        var report = CloudCollaborationPhaseGateReport.Combine([]);

        Assert.IsFalse(report.AllPassed);
    }

    [TestMethod]
    public void AllPassed_OneRequirementIdMissing_IsFalse()
    {
        var evidence = CloudCollaborationPhaseGateReport.RequiredRequirementIds
            .Where(id => id != "VAULT-005")
            .Select(id => Evidence(id, CloudCollaborationPhaseGateCategory.Vault));

        var report = CloudCollaborationPhaseGateReport.Combine(evidence);

        Assert.IsFalse(report.AllPassed);
        CollectionAssert.Contains(report.MissingRequirementIds.ToList(), "VAULT-005");
    }

    [TestMethod]
    public void AllPassed_OnlyOneCategoryRepresented_IsFalse()
    {
        // A report that covers every requirement ID by name but never actually exercises the Sharing
        // or Vault surfaces at all must never silently pass -- the same "a category with zero fixtures
        // can never pass" discipline CloudFidelityPhaseGateReportTests proves for Icon/Appraisal.
        var evidence = CloudCollaborationPhaseGateReport.RequiredRequirementIds
            .Select(id => Evidence(id, CloudCollaborationPhaseGateCategory.Offer));

        var report = CloudCollaborationPhaseGateReport.Combine(evidence);

        Assert.IsFalse(report.AllPassed);
    }

    [TestMethod]
    public void MissingRequirementIds_IsBlockingRatherThanANonBlockingGap()
    {
        var evidence = CloudCollaborationPhaseGateReport.RequiredRequirementIds
            .Where(id => id != "SHARE-004")
            .Select(id => Evidence(id, id.StartsWith("XFER") ? CloudCollaborationPhaseGateCategory.Offer
                : id.StartsWith("SHARE") ? CloudCollaborationPhaseGateCategory.Sharing
                : CloudCollaborationPhaseGateCategory.Vault));

        var report = CloudCollaborationPhaseGateReport.Combine(evidence, nonBlockingGaps: ["Marketplace cross-cutting E2E not yet in scope (P7)."]);

        Assert.IsFalse(report.AllPassed);
        CollectionAssert.Contains(report.MissingRequirementIds.ToList(), "SHARE-004");
        CollectionAssert.DoesNotContain(report.NonBlockingGaps.ToList(), "SHARE-004");
    }

    [TestMethod]
    public void CoveredRequirementIds_DeduplicatesMultipleEvidenceForTheSameRequirement()
    {
        var report = CloudCollaborationPhaseGateReport.Combine(
        [
            Evidence("XFER-002", CloudCollaborationPhaseGateCategory.Offer),
            Evidence("XFER-002", CloudCollaborationPhaseGateCategory.Offer),
        ]);

        Assert.HasCount(1, report.CoveredRequirementIds);
    }

    [TestMethod]
    public void EvidenceCountByCategory_CountsEachCategoryIndependently()
    {
        var report = CloudCollaborationPhaseGateReport.Combine(
        [
            Evidence("XFER-001", CloudCollaborationPhaseGateCategory.Offer),
            Evidence("XFER-002", CloudCollaborationPhaseGateCategory.Offer),
            Evidence("VAULT-001", CloudCollaborationPhaseGateCategory.Vault),
        ]);

        Assert.AreEqual(2, report.EvidenceCountByCategory[CloudCollaborationPhaseGateCategory.Offer]);
        Assert.AreEqual(1, report.EvidenceCountByCategory[CloudCollaborationPhaseGateCategory.Vault]);
    }

    [TestMethod]
    public void CloudCollaborationPhaseGateReport_JsonRoundTrips()
    {
        var report = CloudCollaborationPhaseGateReport.Combine(
            [Evidence("XFER-001", CloudCollaborationPhaseGateCategory.Offer)],
            nonBlockingGaps: ["gap"]);

        var json = JsonSerializer.Serialize(report);
        var roundTripped = JsonSerializer.Deserialize<CloudCollaborationPhaseGateReport>(json);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(1, roundTripped!.CoveredRequirementIds.Count);
        CollectionAssert.Contains(roundTripped.NonBlockingGaps.ToList(), "gap");
    }
}
