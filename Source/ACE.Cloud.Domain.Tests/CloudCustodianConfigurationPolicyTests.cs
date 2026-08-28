namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Table-driven coverage for <see cref="CloudCustodianConfigurationPolicy"/> (DEP-007's "default
/// Marketplace and mansion sets, independent toggles, custom full ACE position strings,
/// duplicate/invalid positions" Red tests).
/// </summary>
[TestClass]
public sealed class CloudCustodianConfigurationPolicyTests
{
    private const string PositionA = "0x00030146 [122.346077 -88.811691 -11.995001] 0.181943 0.000000 0.000000 -0.983309";
    private const string PositionB = "0x0000A9FE [50.000000 60.000000 0.000000] 1.000000 0.000000 0.000000 0.000000";

    [TestMethod]
    public void Default_EnablesBothSharedSetsAndHasNoCustomPositions()
    {
        var configuration = CloudCustodianConfiguration.Default();

        Assert.IsTrue(configuration.MarketplaceEnabled);
        Assert.IsTrue(configuration.MansionsEnabled);
        Assert.HasCount(0, configuration.CustomPositions);
        Assert.AreEqual(CloudAggregateVersion.Initial, configuration.Version);
    }

    [TestMethod]
    public void SetMarketplaceEnabled_ToADifferentValue_TogglesIndependentlyOfMansions()
    {
        var configuration = CloudCustodianConfiguration.Default();

        var result = CloudCustodianConfigurationPolicy.SetMarketplaceEnabled(configuration, enabled: false);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Configuration!.MarketplaceEnabled);
        Assert.IsTrue(result.Configuration.MansionsEnabled, "Toggling Marketplace must not affect the independent Mansion toggle.");
        Assert.AreEqual(configuration.Version.Next(), result.Configuration.Version);
    }

    [TestMethod]
    public void SetMansionsEnabled_ToADifferentValue_TogglesIndependentlyOfMarketplace()
    {
        var configuration = CloudCustodianConfiguration.Default();

        var result = CloudCustodianConfigurationPolicy.SetMansionsEnabled(configuration, enabled: false);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Configuration!.MansionsEnabled);
        Assert.IsTrue(result.Configuration.MarketplaceEnabled);
        Assert.AreEqual(configuration.Version.Next(), result.Configuration.Version);
    }

    [TestMethod]
    public void SetMarketplaceEnabled_ToItsCurrentValue_IsANoOpThatDoesNotBumpVersion()
    {
        var configuration = CloudCustodianConfiguration.Default();

        var result = CloudCustodianConfigurationPolicy.SetMarketplaceEnabled(configuration, enabled: true);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(configuration.Version, result.Configuration!.Version, "A same-value toggle must not invalidate open sell windows.");
    }

    [TestMethod]
    public void AddCustomPosition_AValidUnusedPosition_SucceedsAndBumpsVersion()
    {
        var configuration = CloudCustodianConfiguration.Default();
        var id = Guid.NewGuid();

        var result = CloudCustodianConfigurationPolicy.AddCustomPosition(configuration, id, PositionA);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, result.Configuration!.CustomPositions);
        Assert.AreEqual(id, result.Configuration.CustomPositions[0].Id);
        Assert.AreEqual(PositionA, result.Configuration.CustomPositions[0].Position.Raw);
        Assert.AreEqual(configuration.Version.Next(), result.Configuration.Version);
    }

    [TestMethod]
    public void AddCustomPosition_AnInvalidPositionString_IsRejectedAndDoesNotChangeTheConfiguration()
    {
        var configuration = CloudCustodianConfiguration.Default();

        var result = CloudCustodianConfigurationPolicy.AddCustomPosition(configuration, Guid.NewGuid(), "not a position");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNull(result.Configuration);
        Assert.HasCount(0, configuration.CustomPositions);
    }

    [TestMethod]
    public void AddCustomPosition_ADuplicateOfAnExistingCustomPosition_IsRejected()
    {
        var configuration = CloudCustodianConfiguration.Default();
        var firstAdd = CloudCustodianConfigurationPolicy.AddCustomPosition(configuration, Guid.NewGuid(), PositionA);
        Assert.IsTrue(firstAdd.IsSuccess);

        var secondAdd = CloudCustodianConfigurationPolicy.AddCustomPosition(firstAdd.Configuration!, Guid.NewGuid(), PositionA);

        Assert.IsFalse(secondAdd.IsSuccess);
        Assert.HasCount(1, firstAdd.Configuration!.CustomPositions);
    }

    [TestMethod]
    public void RemoveCustomPosition_AnExistingId_RemovesItAndBumpsVersion()
    {
        var configuration = CloudCustodianConfiguration.Default();
        var id = Guid.NewGuid();
        var added = CloudCustodianConfigurationPolicy.AddCustomPosition(configuration, id, PositionA).Configuration!;

        var result = CloudCustodianConfigurationPolicy.RemoveCustomPosition(added, id);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(0, result.Configuration!.CustomPositions);
        Assert.AreEqual(added.Version.Next(), result.Configuration.Version);
    }

    [TestMethod]
    public void RemoveCustomPosition_AnUnknownId_IsRejected()
    {
        var configuration = CloudCustodianConfiguration.Default();

        var result = CloudCustodianConfigurationPolicy.RemoveCustomPosition(configuration, Guid.NewGuid());

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void AddCustomPosition_ASecondDifferentPosition_BothCoexist()
    {
        var configuration = CloudCustodianConfiguration.Default();
        var withA = CloudCustodianConfigurationPolicy.AddCustomPosition(configuration, Guid.NewGuid(), PositionA).Configuration!;

        var result = CloudCustodianConfigurationPolicy.AddCustomPosition(withA, Guid.NewGuid(), PositionB);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(2, result.Configuration!.CustomPositions);
    }
}
