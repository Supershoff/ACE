using System.Text.Json;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Proves the protected comparison harness itself (Red: "Build a protected comparison harness for
/// operator-owned item captures... emits deterministic machine-readable results") using only
/// synthetic fixtures constructed in-process -- the harness's own correctness must not depend on any
/// private capture existing. <see cref="CloudAppraisalGoldenCaptureComparisonTests"/> is the separate
/// test that reads a real operator-owned corpus from disk when one is configured.
/// </summary>
[TestClass]
public sealed class CloudAppraisalGoldenComparisonHarnessTests
{
    private static readonly CloudItemId ItemId = new(1);

    private static CloudAppraisalRawItemSnapshot Snapshot(string name) => new() { ItemId = ItemId, Name = name };

    [TestMethod]
    public void Compare_FixtureThatMatchesTheProjectorsOutput_ReportsMatchWithNoDifferences()
    {
        var snapshot = Snapshot("Golden Item");
        var expectedPanel = CloudAppraisalProjector.Build(snapshot);

        var report = CloudAppraisalGoldenComparisonHarness.Compare(
        [
            new CloudAppraisalGoldenFixture { FixtureName = "golden-item", Snapshot = snapshot, ExpectedPanel = expectedPanel },
        ]);

        Assert.IsTrue(report.AllMatch);
        Assert.AreEqual(CloudAppraisalGoldenComparisonOutcome.Match, report.Results[0].Outcome);
        Assert.IsEmpty(report.Results[0].Differences);
    }

    [TestMethod]
    public void Compare_FixtureWhoseExpectedPanelDiffersFromTheProjectorsOutput_ReportsMismatchWithADiff()
    {
        var snapshot = Snapshot("Golden Item");
        var wrongExpectedPanel = CloudAppraisalProjector.Build(snapshot) with { ItemName = "Not The Real Name" };

        var report = CloudAppraisalGoldenComparisonHarness.Compare(
        [
            new CloudAppraisalGoldenFixture { FixtureName = "golden-item", Snapshot = snapshot, ExpectedPanel = wrongExpectedPanel },
        ]);

        Assert.IsFalse(report.AllMatch);
        Assert.AreEqual(CloudAppraisalGoldenComparisonOutcome.Mismatch, report.Results[0].Outcome);
        Assert.IsTrue(report.Results[0].Differences.Any(d => d.Contains("ItemName")));
    }

    [TestMethod]
    public void Compare_MultipleFixtures_ReportsOneResultPerFixtureInOrder()
    {
        var matching = Snapshot("Match Item");
        var mismatching = Snapshot("Mismatch Item");

        var report = CloudAppraisalGoldenComparisonHarness.Compare(
        [
            new CloudAppraisalGoldenFixture { FixtureName = "first", Snapshot = matching, ExpectedPanel = CloudAppraisalProjector.Build(matching) },
            new CloudAppraisalGoldenFixture { FixtureName = "second", Snapshot = mismatching, ExpectedPanel = CloudAppraisalProjector.Build(mismatching) with { ItemName = "Wrong" } },
        ]);

        Assert.HasCount(2, report.Results);
        Assert.AreEqual("first", report.Results[0].FixtureName);
        Assert.AreEqual(CloudAppraisalGoldenComparisonOutcome.Match, report.Results[0].Outcome);
        Assert.AreEqual("second", report.Results[1].FixtureName);
        Assert.AreEqual(CloudAppraisalGoldenComparisonOutcome.Mismatch, report.Results[1].Outcome);
        Assert.IsFalse(report.AllMatch);
    }

    [TestMethod]
    public void Compare_NullFixtureList_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => CloudAppraisalGoldenComparisonHarness.Compare(null!));
    }

    [TestMethod]
    public void CloudAppraisalGoldenFixture_RoundTripsThroughJsonForOperatorCaptureFiles()
    {
        // #28's operator-owned capture corpus is expected to be deserialized JSON fed straight into
        // CloudAppraisalGoldenComparisonHarness.Compare; this proves the fixture contract actually
        // round-trips through the serializer #28 would use to read such a file from disk.
        var snapshot = Snapshot("Round Trip Item") with
        {
            Value = 42,
            Spells = [new CloudAppraisalSpellReference { Name = "Some Spell", IsActiveEnchantment = true }],
        };
        var fixture = new CloudAppraisalGoldenFixture
        {
            FixtureName = "round-trip",
            Snapshot = snapshot,
            ExpectedPanel = CloudAppraisalProjector.Build(snapshot),
        };

        var json = JsonSerializer.Serialize(fixture);
        var roundTripped = JsonSerializer.Deserialize<CloudAppraisalGoldenFixture>(json);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(fixture.FixtureName, roundTripped!.FixtureName);
        Assert.AreEqual(fixture.ExpectedPanel, roundTripped.ExpectedPanel);

        var report = CloudAppraisalGoldenComparisonHarness.Compare([roundTripped]);
        Assert.IsTrue(report.AllMatch);
    }
}
