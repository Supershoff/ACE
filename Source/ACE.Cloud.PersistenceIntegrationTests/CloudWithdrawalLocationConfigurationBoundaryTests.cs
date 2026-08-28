using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #16's versioned Withdrawal Landblock configuration persistence
/// (WDR-006, ADM-003): default (empty, withdraw-anywhere off) state, named-landblock add/remove,
/// duplicate/invalid rejection, the withdraw-anywhere toggle, optimistic-concurrency hot-apply, and
/// restart persistence (a fresh <see cref="CloudDbContext"/>/connection, simulating ACE restarting,
/// must still see every committed change).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudWithdrawalLocationConfigurationBoundaryTests
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

    private CloudWithdrawalLocationConfigurationBoundary NewBoundary(out CloudDbContext context)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        context = new CloudDbContext(options);
        return new CloudWithdrawalLocationConfigurationBoundary(context);
    }

    [TestMethod]
    public async Task GetCurrent_OnFirstEverRead_BootstrapsTheDefaultConfiguration()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var configuration = await boundary.GetCurrentAsync(ShardId);

        Assert.IsFalse(configuration.WithdrawAnywhereEnabled, "WDR-006: withdraw anywhere defaults off.");
        Assert.HasCount(0, configuration.NamedLandblocks);
        Assert.AreEqual(CloudAggregateVersion.Initial, configuration.Version);
    }

    [TestMethod]
    public async Task SetWithdrawAnywhereEnabled_PersistsAcrossASimulatedRestart()
    {
        await using (var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var boundary = new CloudWithdrawalLocationConfigurationBoundary(context);
            var initial = await boundary.GetCurrentAsync(ShardId);

            var outcome = await boundary.SetWithdrawAnywhereEnabledAsync(ShardId, enabled: true, initial.Version.Value);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        }

        // "Restart": a brand-new context/connection (WDR-006/ADM-003: "persist while ACE is down").
        await using var restarted = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var restartedBoundary = new CloudWithdrawalLocationConfigurationBoundary(restarted);

        var configuration = await restartedBoundary.GetCurrentAsync(ShardId);

        Assert.IsTrue(configuration.WithdrawAnywhereEnabled);
    }

    [TestMethod]
    public async Task AddNamedLandblock_ThenRemoveIt_RoundTripsAndBumpsVersionEachTime()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var v1 = await boundary.GetCurrentAsync(ShardId);

        var addOutcome = await boundary.AddNamedLandblockAsync(ShardId, 0x123E, "Town Hall", v1.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, addOutcome.Kind);
        var v2 = addOutcome.Value!;
        Assert.HasCount(1, v2.NamedLandblocks);
        Assert.AreEqual((ushort)0x123E, v2.NamedLandblocks[0].Landblock);
        Assert.AreEqual(v1.Version.Next(), v2.Version);

        var landblockId = v2.NamedLandblocks[0].Id;
        var removeOutcome = await boundary.RemoveNamedLandblockAsync(ShardId, landblockId, v2.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, removeOutcome.Kind);
        Assert.HasCount(0, removeOutcome.Value!.NamedLandblocks);
        Assert.AreEqual(v2.Version.Next(), removeOutcome.Value.Version);
    }

    [TestMethod]
    public async Task AddNamedLandblock_DuplicateLandblock_ReturnsConflict()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var v1 = await boundary.GetCurrentAsync(ShardId);
        var first = await boundary.AddNamedLandblockAsync(ShardId, 0x123E, "Town Hall", v1.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.AddNamedLandblockAsync(ShardId, 0x123E, "Duplicate", first.Value!.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, second.Kind);
    }

    [TestMethod]
    public async Task Mutation_WithAStaleExpectedVersion_ReturnsConflict()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var v1 = await boundary.GetCurrentAsync(ShardId);
        await boundary.SetWithdrawAnywhereEnabledAsync(ShardId, enabled: true, v1.Version.Value);

        var staleOutcome = await boundary.SetWithdrawAnywhereEnabledAsync(ShardId, enabled: false, v1.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, staleOutcome.Kind);
    }
}
