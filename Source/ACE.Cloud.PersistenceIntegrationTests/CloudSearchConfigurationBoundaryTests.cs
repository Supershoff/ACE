using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #32's SRCH-001 admin-disablement half: Safe Regex Search
/// configuration persistence and admin-only changes, matching
/// <see cref="CloudMarketplaceConfigurationBoundaryTests"/>'s established shape for a singleton
/// admin-config aggregate.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudSearchConfigurationBoundaryTests
{
    private const string ShardId = "us1";
    private const uint AdminAccessLevel = 5;
    private const uint NonAdminAccessLevel = 1;

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

    private CloudSearchConfigurationBoundary NewBoundary(out CloudDbContext context)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        context = new CloudDbContext(options);
        return new CloudSearchConfigurationBoundary(context);
    }

    [TestMethod]
    public async Task GetCurrent_OnFirstEverRead_BootstrapsRegexSearchEnabled()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var configuration = await boundary.GetCurrentAsync(ShardId);

        Assert.IsTrue(configuration.RegexSearchEnabled);
        Assert.AreEqual(CloudAggregateVersion.Initial, configuration.Version);
    }

    [TestMethod]
    public async Task SetRegexSearchEnabled_ByAnAdmin_Succeeds_AndPersistsAcrossASimulatedRestart()
    {
        await using (var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var boundary = new CloudSearchConfigurationBoundary(context);
            var initial = await boundary.GetCurrentAsync(ShardId);

            var outcome = await boundary.SetRegexSearchEnabledAsync(ShardId, requested: false, AdminAccessLevel, initial.Version.Value);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        }

        await using var restarted = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var restartedBoundary = new CloudSearchConfigurationBoundary(restarted);
        var configuration = await restartedBoundary.GetCurrentAsync(ShardId);

        Assert.IsFalse(configuration.RegexSearchEnabled);
    }

    [TestMethod]
    public async Task SetRegexSearchEnabled_ByANonAdmin_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var outcome = await boundary.SetRegexSearchEnabledAsync(ShardId, requested: false, NonAdminAccessLevel, initial.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task SetRegexSearchEnabled_WithStaleExpectedVersion_IsRejectedAsAConflict()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await boundary.SetRegexSearchEnabledAsync(ShardId, requested: false, AdminAccessLevel, initial.Version.Value)).Kind);

        // Retrying with the now-stale version must not silently reapply on top of the new state.
        var staleOutcome = await boundary.SetRegexSearchEnabledAsync(ShardId, requested: true, AdminAccessLevel, initial.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, staleOutcome.Kind);
    }
}
