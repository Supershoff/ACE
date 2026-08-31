using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.WorldObjects;

namespace ACE.Cloud.ServerSeamsTests;

/// <summary>
/// Regression coverage for the live acceptance defects found at the phase-five human gate: a
/// dynamically spawned Custodian must not decay like a dropped item, its client vendor pane must
/// permit rows to reach the Cloud eligibility policy, and an operator-selected base vendor must
/// not leak its ordinary merchandise into the Custodian.
/// </summary>
[TestClass]
public sealed class CloudCustodianRuntimePolicyTests
{
    [TestMethod]
    public void RuntimePresentation_IsPermanentAndLetsTheServerEvaluateEveryClientRow()
    {
        Assert.AreEqual(-1d, CloudCustodianRuntimePolicy.NeverRot);
        Assert.AreEqual(uint.MaxValue, unchecked((uint)CloudCustodianRuntimePolicy.ClientAcceptedItemTypes));
    }

    [TestMethod]
    public void RemoveInheritedShopInventory_RemovesOnlyVendorStock()
    {
        var wielded = new PropertiesCreateList { DestinationType = DestinationType.Wield, WeenieClassId = 100 };
        var shopItem = new PropertiesCreateList { DestinationType = DestinationType.Shop, WeenieClassId = 200 };
        ICollection<PropertiesCreateList> createList = [wielded, shopItem];

        CloudCustodianRuntimePolicy.RemoveInheritedShopInventory(createList);

        Assert.HasCount(1, createList);
        Assert.AreSame(wielded, createList.Single());
    }

    [TestMethod]
    public void RemoveInheritedShopInventory_AllowsTemplatesWithoutACreateList()
    {
        CloudCustodianRuntimePolicy.RemoveInheritedShopInventory(null!);
    }
}
