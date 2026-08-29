using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #28's Green requirement: "A documented local-only command generates/validates the icon and
/// appraisal fixture contracts." Proves <see cref="CloudFixtureContractValidator"/> accepts a
/// generator-produced fixture and rejects one an operator (or a merge) hand-tampered with afterward.
/// </summary>
[TestClass]
public sealed class CloudFixtureContractValidatorTests
{
    private static readonly CloudItemId ItemId = new(1);

    [TestMethod]
    public void ValidateIconFixture_GeneratorProducedFixture_HasNoProblems()
    {
        var fixture = CloudIconFixtureGenerator.GenerateFromHash("clothing-variant", new CloudIconCompositionInputs { BaseIconDid = 1 }, new string('a', 64));

        var problems = CloudFixtureContractValidator.ValidateIconFixture(fixture);

        Assert.IsEmpty(problems);
    }

    [TestMethod]
    public void ValidateIconFixture_HashIsNotSixtyFourHexCharacters_ReportsAProblem()
    {
        var fixture = new CloudIconGoldenFixture
        {
            FixtureName = "bad-hash",
            Inputs = new CloudIconCompositionInputs { BaseIconDid = 1 },
            ExpectedPngSha256Hex = "too-short",
        };

        var problems = CloudFixtureContractValidator.ValidateIconFixture(fixture);

        Assert.IsGreaterThan(0, problems.Count);
    }

    [TestMethod]
    public void ValidateIconFixture_FixtureNameLooksLikeAPath_ReportsAProblem()
    {
        var fixture = new CloudIconGoldenFixture
        {
            FixtureName = "../escaping",
            Inputs = new CloudIconCompositionInputs { BaseIconDid = 1 },
            ExpectedPngSha256Hex = new string('a', 64),
        };

        var problems = CloudFixtureContractValidator.ValidateIconFixture(fixture);

        Assert.IsGreaterThan(0, problems.Count);
    }

    [TestMethod]
    public void ValidateAppraisalFixture_GeneratorProducedFixture_HasNoProblems()
    {
        var fixture = CloudAppraisalFixtureGenerator.Generate("test-buckler", new CloudAppraisalRawItemSnapshot { ItemId = ItemId, Name = "Test Buckler" });

        var problems = CloudFixtureContractValidator.ValidateAppraisalFixture(fixture);

        Assert.IsEmpty(problems);
    }

    [TestMethod]
    public void ValidateAppraisalFixture_ExpectedPanelWasHandTamperedAfterGeneration_ReportsAProblem()
    {
        var fixture = CloudAppraisalFixtureGenerator.Generate("test-buckler", new CloudAppraisalRawItemSnapshot { ItemId = ItemId, Name = "Test Buckler" });
        var tampered = fixture with { ExpectedPanel = fixture.ExpectedPanel with { ItemName = "Hand-Edited Name" } };

        var problems = CloudFixtureContractValidator.ValidateAppraisalFixture(tampered);

        Assert.IsGreaterThan(0, problems.Count);
    }
}
