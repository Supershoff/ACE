using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green tests for issue #23's MKT-203/MKT-204 section: Enabled permits all Marketplace
/// actions; Disabled blocks only new listings; Maintenance Frozen blocks all Marketplace mutations and
/// clock progress.
/// </summary>
[TestClass]
public sealed class CloudMarketplaceStatePolicyTests
{
    private const uint AdminAccessLevel = 5;
    private const uint NonAdminAccessLevel = 1;

    [TestMethod]
    public void SetState_ByAdmin_ToADifferentState_Succeeds_AndBumpsVersion()
    {
        var current = CloudMarketplaceConfiguration.Default();

        var result = CloudMarketplaceStatePolicy.SetState(current, CloudMarketplaceState.Disabled, AdminAccessLevel);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(CloudMarketplaceState.Disabled, result.Configuration!.State);
        Assert.AreEqual(current.Version.Next(), result.Configuration.Version);
    }

    [TestMethod]
    public void SetState_ToTheSameState_IsANoOp_AndDoesNotBumpVersion()
    {
        var current = CloudMarketplaceConfiguration.Default();

        var result = CloudMarketplaceStatePolicy.SetState(current, CloudMarketplaceState.Enabled, AdminAccessLevel);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(current.Version, result.Configuration!.Version);
    }

    [TestMethod]
    public void SetState_ByNonAdmin_IsRejected()
    {
        var current = CloudMarketplaceConfiguration.Default();

        var result = CloudMarketplaceStatePolicy.SetState(current, CloudMarketplaceState.Disabled, NonAdminAccessLevel);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void CanPublishNewListing_OnlyWhenEnabled()
    {
        Assert.IsTrue(CloudMarketplaceStatePolicy.CanPublishNewListing(CloudMarketplaceState.Enabled));
        Assert.IsFalse(CloudMarketplaceStatePolicy.CanPublishNewListing(CloudMarketplaceState.Disabled));
        Assert.IsFalse(CloudMarketplaceStatePolicy.CanPublishNewListing(CloudMarketplaceState.MaintenanceFrozen));
    }

    [TestMethod]
    public void CanContinueExistingAuctionActivity_TrueUnlessMaintenanceFrozen()
    {
        Assert.IsTrue(CloudMarketplaceStatePolicy.CanContinueExistingAuctionActivity(CloudMarketplaceState.Enabled));
        Assert.IsTrue(
            CloudMarketplaceStatePolicy.CanContinueExistingAuctionActivity(CloudMarketplaceState.Disabled),
            "MKT-203: Disabled blocks only new listings; existing auctions may still bid, use Buy It Now, close, and settle.");
        Assert.IsFalse(CloudMarketplaceStatePolicy.CanContinueExistingAuctionActivity(CloudMarketplaceState.MaintenanceFrozen));
    }

    [TestMethod]
    public void BlocksClockProgress_OnlyWhenMaintenanceFrozen()
    {
        Assert.IsFalse(CloudMarketplaceStatePolicy.BlocksClockProgress(CloudMarketplaceState.Enabled));
        Assert.IsFalse(CloudMarketplaceStatePolicy.BlocksClockProgress(CloudMarketplaceState.Disabled));
        Assert.IsTrue(CloudMarketplaceStatePolicy.BlocksClockProgress(CloudMarketplaceState.MaintenanceFrozen));
    }
}
