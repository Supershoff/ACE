namespace ACE.Cloud.Domain.Tests;

/// <summary>Table-driven coverage for <see cref="CloudWithdrawalLocationConfigurationPolicy"/> (WDR-006, ADM-003).</summary>
[TestClass]
public sealed class CloudWithdrawalLocationConfigurationPolicyTests
{
    [TestMethod]
    public void Default_HasNoNamedLandblocksAndWithdrawAnywhereDisabled()
    {
        var configuration = CloudWithdrawalLocationConfiguration.Default();

        Assert.IsFalse(configuration.WithdrawAnywhereEnabled);
        Assert.HasCount(0, configuration.NamedLandblocks);
        Assert.AreEqual(CloudAggregateVersion.Initial, configuration.Version);
    }

    [TestMethod]
    public void SetWithdrawAnywhereEnabled_ToADifferentValue_BumpsVersion()
    {
        var configuration = CloudWithdrawalLocationConfiguration.Default();

        var result = CloudWithdrawalLocationConfigurationPolicy.SetWithdrawAnywhereEnabled(configuration, enabled: true);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.Configuration!.WithdrawAnywhereEnabled);
        Assert.AreEqual(configuration.Version.Next(), result.Configuration.Version);
    }

    [TestMethod]
    public void SetWithdrawAnywhereEnabled_ToTheSameValue_IsANoOpThatDoesNotBumpVersion()
    {
        var configuration = CloudWithdrawalLocationConfiguration.Default();

        var result = CloudWithdrawalLocationConfigurationPolicy.SetWithdrawAnywhereEnabled(configuration, enabled: false);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(configuration.Version, result.Configuration!.Version);
    }

    [TestMethod]
    public void AddNamedLandblock_NewLandblock_Succeeds()
    {
        var configuration = CloudWithdrawalLocationConfiguration.Default();

        var result = CloudWithdrawalLocationConfigurationPolicy.AddNamedLandblock(configuration, Guid.NewGuid(), 0x123E, "Town Hall");

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(1, result.Configuration!.NamedLandblocks);
        Assert.AreEqual((ushort)0x123E, result.Configuration.NamedLandblocks[0].Landblock);
        Assert.AreEqual("Town Hall", result.Configuration.NamedLandblocks[0].Name);
        Assert.AreEqual(configuration.Version.Next(), result.Configuration.Version);
    }

    [TestMethod]
    public void AddNamedLandblock_DuplicateLandblock_Fails()
    {
        var configuration = CloudWithdrawalLocationConfiguration.Default();
        var first = CloudWithdrawalLocationConfigurationPolicy.AddNamedLandblock(configuration, Guid.NewGuid(), 0x123E, "Town Hall");

        var second = CloudWithdrawalLocationConfigurationPolicy.AddNamedLandblock(first.Configuration!, Guid.NewGuid(), 0x123E, "Duplicate");

        Assert.IsFalse(second.IsSuccess);
        Assert.HasCount(1, first.Configuration!.NamedLandblocks);
    }

    [TestMethod]
    public void AddNamedLandblock_EmptyName_Fails()
    {
        var configuration = CloudWithdrawalLocationConfiguration.Default();

        var result = CloudWithdrawalLocationConfigurationPolicy.AddNamedLandblock(configuration, Guid.NewGuid(), 0x123E, "  ");

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void RemoveNamedLandblock_ExistingLandblock_Succeeds()
    {
        var configuration = CloudWithdrawalLocationConfiguration.Default();
        var added = CloudWithdrawalLocationConfigurationPolicy.AddNamedLandblock(configuration, Guid.NewGuid(), 0x123E, "Town Hall");
        var landblockId = added.Configuration!.NamedLandblocks[0].Id;

        var result = CloudWithdrawalLocationConfigurationPolicy.RemoveNamedLandblock(added.Configuration, landblockId);

        Assert.IsTrue(result.IsSuccess);
        Assert.HasCount(0, result.Configuration!.NamedLandblocks);
    }

    [TestMethod]
    public void RemoveNamedLandblock_UnknownId_Fails()
    {
        var configuration = CloudWithdrawalLocationConfiguration.Default();

        var result = CloudWithdrawalLocationConfigurationPolicy.RemoveNamedLandblock(configuration, Guid.NewGuid());

        Assert.IsFalse(result.IsSuccess);
    }
}
