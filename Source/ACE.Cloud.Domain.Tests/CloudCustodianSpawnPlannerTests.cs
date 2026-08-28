namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Coverage for <see cref="CloudCustodianSpawnPlanner.Plan"/> (Green: "Spawn/despawn or reconfigure
/// Custodians safely on the world thread"; DEP-008: "apply without an ACE restart").
/// </summary>
[TestClass]
public sealed class CloudCustodianSpawnPlannerTests
{
    private static readonly CloudCustodianPosition PositionA =
        CloudCustodianPosition.TryParse("0x00030146 [1.000000 2.000000 3.000000] 1.000000 0.000000 0.000000 0.000000")!;

    private static readonly CloudCustodianPosition PositionB =
        CloudCustodianPosition.TryParse("0x00030147 [4.000000 5.000000 6.000000] 1.000000 0.000000 0.000000 0.000000")!;

    [TestMethod]
    public void Plan_NothingCurrentlySpawned_SpawnsEveryDesiredLocation()
    {
        var desired = new[]
        {
            new CloudCustodianLocation(CloudCustodianLocationKey.Marketplace, PositionA),
        };

        var plan = CloudCustodianSpawnPlanner.Plan(desired, currentlySpawnedKeys: []);

        Assert.HasCount(1, plan.ToSpawn);
        Assert.AreEqual(CloudCustodianLocationKey.Marketplace, plan.ToSpawn[0].Key);
        Assert.HasCount(0, plan.ToDespawn);
    }

    [TestMethod]
    public void Plan_ALocationNoLongerDesired_IsDespawned()
    {
        var plan = CloudCustodianSpawnPlanner.Plan(
            desiredLocations: [], currentlySpawnedKeys: [CloudCustodianLocationKey.Marketplace]);

        Assert.HasCount(0, plan.ToSpawn);
        CollectionAssert.Contains(plan.ToDespawn.ToList(), CloudCustodianLocationKey.Marketplace);
    }

    [TestMethod]
    public void Plan_ALocationStillDesired_IsLeftAlone()
    {
        var desired = new[] { new CloudCustodianLocation(CloudCustodianLocationKey.Marketplace, PositionA) };

        var plan = CloudCustodianSpawnPlanner.Plan(desired, currentlySpawnedKeys: [CloudCustodianLocationKey.Marketplace]);

        Assert.HasCount(0, plan.ToSpawn, "An unchanged location must not be respawned.");
        Assert.HasCount(0, plan.ToDespawn, "An unchanged location must not be despawned.");
    }

    [TestMethod]
    public void Plan_OneLocationAddedAndAnotherRemoved_OnlyThoseTwoChange()
    {
        var customIdKept = CloudCustodianLocationKey.ForCustom(Guid.NewGuid());
        var customIdAdded = CloudCustodianLocationKey.ForCustom(Guid.NewGuid());

        var desired = new[]
        {
            new CloudCustodianLocation(customIdKept, PositionA),
            new CloudCustodianLocation(customIdAdded, PositionB),
        };

        var currentlySpawned = new[] { customIdKept, CloudCustodianLocationKey.Marketplace };

        var plan = CloudCustodianSpawnPlanner.Plan(desired, currentlySpawned);

        Assert.HasCount(1, plan.ToSpawn);
        Assert.AreEqual(customIdAdded, plan.ToSpawn[0].Key);
        Assert.HasCount(1, plan.ToDespawn);
        Assert.AreEqual(CloudCustodianLocationKey.Marketplace, plan.ToDespawn[0]);
    }

    [TestMethod]
    public void Plan_EditingACustomPositionsCoordinates_DespawnsTheOldOneAndSpawnsANewOne()
    {
        // Editing is modeled as remove-then-add with a fresh ID (CloudCustodianConfigurationPolicy
        // has no "update" operation), so the planner must treat it as two independent identity
        // changes, never an in-place move of the live NPC.
        var oldId = CloudCustodianLocationKey.ForCustom(Guid.NewGuid());
        var newId = CloudCustodianLocationKey.ForCustom(Guid.NewGuid());

        var plan = CloudCustodianSpawnPlanner.Plan(
            desiredLocations: [new CloudCustodianLocation(newId, PositionB)],
            currentlySpawnedKeys: [oldId]);

        Assert.HasCount(1, plan.ToSpawn);
        Assert.AreEqual(newId, plan.ToSpawn[0].Key);
        Assert.HasCount(1, plan.ToDespawn);
        Assert.AreEqual(oldId, plan.ToDespawn[0]);
    }
}
