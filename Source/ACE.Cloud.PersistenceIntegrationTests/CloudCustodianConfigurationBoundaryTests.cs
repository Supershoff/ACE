using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #12's versioned Custodian configuration persistence (DEP-007,
/// DEP-008, ADM-003): default Marketplace/Mansion sets, independent toggles, custom position
/// add/remove, duplicate/invalid rejection, optimistic-concurrency hot-apply, and restart
/// persistence (a fresh <see cref="CloudDbContext"/>/connection, simulating ACE restarting, must
/// still see every committed change).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodianConfigurationBoundaryTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await CloudBoundaryTestFixtureData.ResetAsync(_fixture.CloudConnectionString, ShardId);
    }

    private CloudCustodianConfigurationBoundary NewBoundary(out CloudDbContext context)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        context = new CloudDbContext(options);
        return new CloudCustodianConfigurationBoundary(context);
    }

    [TestMethod]
    public async Task GetCurrent_OnFirstEverRead_BootstrapsTheDefaultConfiguration()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var configuration = await boundary.GetCurrentAsync(ShardId);

        Assert.IsTrue(configuration.MarketplaceEnabled);
        Assert.IsTrue(configuration.MansionsEnabled);
        Assert.HasCount(0, configuration.CustomPositions);
        Assert.AreEqual(CloudAggregateVersion.Initial, configuration.Version);
    }

    [TestMethod]
    public async Task SetMarketplaceEnabled_PersistsAcrossASimulatedRestart()
    {
        await using (var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var boundary = new CloudCustodianConfigurationBoundary(context);
            var initial = await boundary.GetCurrentAsync(ShardId);

            var outcome = await boundary.SetMarketplaceEnabledAsync(ShardId, enabled: false, initial.Version.Value);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        }

        // "Restart": a brand-new context/connection, exactly as a fresh ACE process would open one
        // (DEP-008: "persist while ACE is down").
        await using var restarted = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var restartedBoundary = new CloudCustodianConfigurationBoundary(restarted);

        var configuration = await restartedBoundary.GetCurrentAsync(ShardId);

        Assert.IsFalse(configuration.MarketplaceEnabled);
        Assert.IsTrue(configuration.MansionsEnabled, "Toggling Marketplace must not affect the independent Mansion toggle.");
    }

    [TestMethod]
    public async Task AddCustomPosition_ThenRemoveIt_RoundTripsAndBumpsVersionEachTime()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var v1 = await boundary.GetCurrentAsync(ShardId);

        var addOutcome = await boundary.AddCustomPositionAsync(
            ShardId, "0x00030146 [122.346077 -88.811691 -11.995001] 0.181943 0.000000 0.000000 -0.983309", v1.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, addOutcome.Kind);
        var v2 = addOutcome.Value!;
        Assert.HasCount(1, v2.CustomPositions);
        Assert.AreEqual(v1.Version.Next(), v2.Version);

        var positionId = v2.CustomPositions[0].Id;
        var removeOutcome = await boundary.RemoveCustomPositionAsync(ShardId, positionId, v2.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, removeOutcome.Kind);
        Assert.HasCount(0, removeOutcome.Value!.CustomPositions);
        Assert.AreEqual(v2.Version.Next(), removeOutcome.Value.Version);
    }

    [TestMethod]
    public async Task AddCustomPosition_ADuplicatePosition_IsRejectedAndDoesNotBumpTheVersion()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var v1 = await boundary.GetCurrentAsync(ShardId);
        const string position = "0x00030146 [1.000000 2.000000 3.000000] 1.000000 0.000000 0.000000 0.000000";

        var first = await boundary.AddCustomPositionAsync(ShardId, position, v1.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.AddCustomPositionAsync(ShardId, position, first.Value!.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, second.Kind);

        var current = await boundary.GetCurrentAsync(ShardId);
        Assert.HasCount(1, current.CustomPositions);
        Assert.AreEqual(first.Value.Version, current.Version);
    }

    [TestMethod]
    public async Task AddCustomPosition_AnInvalidPositionString_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var v1 = await boundary.GetCurrentAsync(ShardId);

        var outcome = await boundary.AddCustomPositionAsync(ShardId, "not a real position", v1.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task SetMarketplaceEnabled_WithAStaleExpectedVersion_IsRejectedAsAConflict()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var v1 = await boundary.GetCurrentAsync(ShardId);
        var afterFirstChange = await boundary.SetMarketplaceEnabledAsync(ShardId, enabled: false, v1.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, afterFirstChange.Kind);

        // Retrying against the now-superseded version (DEP-008: a stale caller must not be able to
        // silently overwrite a change it never saw) must be rejected, not silently reapplied.
        var staleRetry = await boundary.SetMansionsEnabledAsync(ShardId, enabled: false, v1.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, staleRetry.Kind);
    }

    [TestMethod]
    public async Task ConcurrentEdits_AgainstTheSameExpectedVersion_OnlyOneCommits()
    {
        var v1 = await NewBoundary(out var seedContext).GetCurrentAsync(ShardId);
        await seedContext.DisposeAsync();

        await using var contextA = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        await using var contextB = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var boundaryA = new CloudCustodianConfigurationBoundary(contextA);
        var boundaryB = new CloudCustodianConfigurationBoundary(contextB);

        var taskA = boundaryA.AddCustomPositionAsync(
            ShardId, "0x00030146 [1.000000 2.000000 3.000000] 1.000000 0.000000 0.000000 0.000000", v1.Version.Value);
        var taskB = boundaryB.AddCustomPositionAsync(
            ShardId, "0x00030147 [4.000000 5.000000 6.000000] 1.000000 0.000000 0.000000 0.000000", v1.Version.Value);

        var results = await Task.WhenAll(taskA, taskB);

        Assert.AreEqual(1, results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Committed), "Exactly one concurrent editor must win.");
        Assert.AreEqual(1, results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Conflict), "The loser must observe a version conflict, never a silent lost update.");

        var final = await NewBoundary(out var finalContext).GetCurrentAsync(ShardId);
        await finalContext.DisposeAsync();
        Assert.HasCount(1, final.CustomPositions, "A concurrent loser must never be able to sneak its change in anyway.");
    }
}
