using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #28's Red requirement: "Add failing tests for the operator fixture-preparation path before
/// implementing it; the operator must not have to hand-author the fixture contracts." Proves
/// <see cref="CloudAppraisalFixtureGenerator"/> derives the entire <see cref="CloudAppraisalGoldenFixture"/>
/// contract -- including the nested <see cref="CloudAppraisalPanel"/> -- from only an operator-owned
/// captured snapshot and a fixture name, so the operator never hand-authors the panel/section/line JSON.
/// </summary>
[TestClass]
public sealed class CloudAppraisalFixtureGeneratorTests
{
    private static readonly CloudItemId ItemId = new(1);

    [TestMethod]
    public void Generate_DerivesTheExpectedPanel_FromTheCapturedSnapshotAlone()
    {
        var capturedSnapshot = new CloudAppraisalRawItemSnapshot { ItemId = ItemId, Name = "Test Buckler" };

        var fixture = CloudAppraisalFixtureGenerator.Generate("test-buckler", capturedSnapshot);

        Assert.AreEqual("test-buckler", fixture.FixtureName);
        Assert.AreEqual(capturedSnapshot, fixture.Snapshot);
        Assert.AreEqual(CloudAppraisalProjector.Build(capturedSnapshot), fixture.ExpectedPanel);
    }

    [TestMethod]
    public void Generate_FixtureRoundTripsThroughTheSharedComparisonHarnessAsAMatch()
    {
        // The generated fixture must be immediately usable by the same harness the protected corpus
        // runs through -- proving the operator never needs to separately author or adjust ExpectedPanel.
        var capturedSnapshot = new CloudAppraisalRawItemSnapshot { ItemId = ItemId, Name = "Sample Dagger", Value = 10 };
        var fixture = CloudAppraisalFixtureGenerator.Generate("sample-dagger", capturedSnapshot);

        var report = CloudAppraisalGoldenComparisonHarness.Compare([fixture]);

        Assert.IsTrue(report.AllMatch);
    }

    [TestMethod]
    [DataRow("with/slash")]
    [DataRow("with\\backslash")]
    [DataRow("../escaping")]
    public void Generate_FixtureNameLooksLikeAPath_Throws(string fixtureName)
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudAppraisalFixtureGenerator.Generate(
            fixtureName, new CloudAppraisalRawItemSnapshot { ItemId = ItemId, Name = "x" }));
    }

    [TestMethod]
    public async Task GenerateAndWriteAsync_WritesAFixtureThatTheSharedLoaderCanLoadBack()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), "cloud-appraisal-fixture-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputDirectory);
        try
        {
            var capturedSnapshot = new CloudAppraisalRawItemSnapshot { ItemId = ItemId, Name = "Round Trip Item" };

            var outputPath = await CloudAppraisalFixtureGenerator.GenerateAndWriteAsync("round-trip-item", capturedSnapshot, outputDirectory);

            Assert.AreEqual(Path.Combine(outputDirectory, "round-trip-item.appraisal.json"), outputPath);

            var loaded = CloudGoldenFixtureLoader.LoadFromDirectory<CloudAppraisalGoldenFixture>(outputDirectory, "*.appraisal.json");
            Assert.HasCount(1, loaded);
            Assert.AreEqual("Round Trip Item", loaded[0].Snapshot.Name);
            Assert.AreEqual(CloudAppraisalProjector.Build(capturedSnapshot), loaded[0].ExpectedPanel);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }
}
