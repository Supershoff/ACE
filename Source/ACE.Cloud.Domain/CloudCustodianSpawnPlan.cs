namespace ACE.Cloud.Domain;

/// <summary>
/// What a hot-apply reapplication must change to bring the live, spawned set of Cloud Custodians
/// back in line with a resolved configuration (DEP-008: "apply without an ACE restart"). Locations
/// present in both sets are left completely alone -- their live NPC keeps its current GUID and
/// in-progress vendor windows -- so an unrelated configuration change never perturbs a Custodian that
/// was not itself added, removed, toggled off, or relocated.
/// </summary>
public sealed record CloudCustodianSpawnPlan(
    IReadOnlyList<CloudCustodianLocation> ToSpawn, IReadOnlyList<CloudCustodianLocationKey> ToDespawn);

/// <summary>
/// Pure diff between a desired resolved location set and the location keys currently spawned in the
/// world (Green: "Spawn/despawn or reconfigure Custodians safely on the world thread"). ACE.Server
/// supplies both sides and executes the resulting plan on the world thread; this class makes no
/// assumption about how or when that execution happens.
/// </summary>
public static class CloudCustodianSpawnPlanner
{
    public static CloudCustodianSpawnPlan Plan(
        IReadOnlyList<CloudCustodianLocation> desiredLocations, IReadOnlyCollection<CloudCustodianLocationKey> currentlySpawnedKeys)
    {
        ArgumentNullException.ThrowIfNull(desiredLocations);
        ArgumentNullException.ThrowIfNull(currentlySpawnedKeys);

        var desiredByKey = desiredLocations.ToDictionary(location => location.Key);

        var toSpawn = desiredLocations.Where(location => !currentlySpawnedKeys.Contains(location.Key)).ToList();
        var toDespawn = currentlySpawnedKeys.Where(key => !desiredByKey.ContainsKey(key)).ToList();

        return new CloudCustodianSpawnPlan(toSpawn, toDespawn);
    }
}
