using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green tests for issue #23's ADM-004 "every mutation gate" requirement, and for the review
/// correction that split the single combined <c>Resolve</c> in two: <see cref="CloudMutationGatePolicy.ResolveGlobal"/>
/// (every non-marketplace mutation) must never freeze on Marketplace Maintenance Frozen alone --
/// IMPLEMENTATION-BRIEF.md calls Global Cloud Maintenance and Marketplace Maintenance Frozen
/// "orthogonal" gates, and MKT-204 scopes the latter to "all Marketplace mutations and clock
/// progress" specifically. <see cref="CloudMutationGatePolicy.ResolveMarketplace"/> keeps the combined
/// behavior for a future marketplace-scoped mutation.
/// </summary>
[TestClass]
public sealed class CloudMutationGatePolicyTests
{
    [TestMethod]
    public void ResolveGlobal_WithNoFreeze_ReturnsOpen()
    {
        var gate = CloudMutationGatePolicy.ResolveGlobal(globalMaintenanceIsFrozen: false);

        Assert.AreEqual(CloudMutationGateState.Open, gate);
    }

    [TestMethod]
    public void ResolveGlobal_WithGlobalMaintenanceFrozen_ReturnsFrozen()
    {
        var gate = CloudMutationGatePolicy.ResolveGlobal(globalMaintenanceIsFrozen: true);

        Assert.AreEqual(CloudMutationGateState.Frozen, gate);
    }

    [TestMethod]
    public void ResolveMarketplace_WithNeitherFreezeActive_ReturnsOpen()
    {
        var gate = CloudMutationGatePolicy.ResolveMarketplace(globalMaintenanceIsFrozen: false, CloudMarketplaceState.Enabled);

        Assert.AreEqual(CloudMutationGateState.Open, gate);
    }

    [TestMethod]
    public void ResolveMarketplace_WithGlobalMaintenanceFrozen_ReturnsFrozen_RegardlessOfMarketplaceState()
    {
        var gate = CloudMutationGatePolicy.ResolveMarketplace(globalMaintenanceIsFrozen: true, CloudMarketplaceState.Enabled);

        Assert.AreEqual(CloudMutationGateState.Frozen, gate);
    }

    [TestMethod]
    public void ResolveMarketplace_WithMarketplaceMaintenanceFrozen_ReturnsFrozen_EvenWithoutGlobalMaintenance()
    {
        var gate = CloudMutationGatePolicy.ResolveMarketplace(globalMaintenanceIsFrozen: false, CloudMarketplaceState.MaintenanceFrozen);

        Assert.AreEqual(CloudMutationGateState.Frozen, gate);
    }

    [TestMethod]
    public void ResolveMarketplace_WithMarketplaceDisabledOnly_ReturnsOpen_DisabledDoesNotFreezeMarketplaceMutations()
    {
        var gate = CloudMutationGatePolicy.ResolveMarketplace(globalMaintenanceIsFrozen: false, CloudMarketplaceState.Disabled);

        Assert.AreEqual(CloudMutationGateState.Open, gate);
    }
}
