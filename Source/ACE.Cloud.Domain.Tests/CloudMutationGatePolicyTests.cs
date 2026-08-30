using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green tests for issue #23's ADM-004 "every mutation gate" requirement: the resolved
/// <see cref="CloudMutationGateState"/> is Frozen if either Global Cloud Maintenance or a Marketplace
/// Maintenance Frozen state currently applies.
/// </summary>
[TestClass]
public sealed class CloudMutationGatePolicyTests
{
    [TestMethod]
    public void Resolve_WithNeitherFreezeActive_ReturnsOpen()
    {
        var gate = CloudMutationGatePolicy.Resolve(globalMaintenanceIsFrozen: false, CloudMarketplaceState.Enabled);

        Assert.AreEqual(CloudMutationGateState.Open, gate);
    }

    [TestMethod]
    public void Resolve_WithGlobalMaintenanceFrozen_ReturnsFrozen_RegardlessOfMarketplaceState()
    {
        var gate = CloudMutationGatePolicy.Resolve(globalMaintenanceIsFrozen: true, CloudMarketplaceState.Enabled);

        Assert.AreEqual(CloudMutationGateState.Frozen, gate);
    }

    [TestMethod]
    public void Resolve_WithMarketplaceMaintenanceFrozen_ReturnsFrozen_EvenWithoutGlobalMaintenance()
    {
        var gate = CloudMutationGatePolicy.Resolve(globalMaintenanceIsFrozen: false, CloudMarketplaceState.MaintenanceFrozen);

        Assert.AreEqual(CloudMutationGateState.Frozen, gate);
    }

    [TestMethod]
    public void Resolve_WithMarketplaceDisabledOnly_ReturnsOpen_DisabledDoesNotFreezeCustodyMutations()
    {
        var gate = CloudMutationGatePolicy.Resolve(globalMaintenanceIsFrozen: false, CloudMarketplaceState.Disabled);

        Assert.AreEqual(CloudMutationGateState.Open, gate);
    }
}
