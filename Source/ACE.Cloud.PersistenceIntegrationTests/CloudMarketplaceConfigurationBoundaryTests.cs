using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #23's MKT-203/MKT-204 section: Marketplace State persistence,
/// admin-only changes, and wiring into the real <see cref="CloudMutationGateReader"/> gate a
/// MaintenanceFrozen Marketplace State also freezes -- exactly like Global Cloud Maintenance.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudMarketplaceConfigurationBoundaryTests
{
    private const string ShardId = "us1";
    private const uint AdminAccessLevel = 5;
    private const uint NonAdminAccessLevel = 1;

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 960_000;

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

    private CloudMarketplaceConfigurationBoundary NewBoundary(out CloudDbContext context)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        context = new CloudDbContext(options);
        return new CloudMarketplaceConfigurationBoundary(context);
    }

    [TestMethod]
    public async Task GetCurrent_OnFirstEverRead_BootstrapsEnabled()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var configuration = await boundary.GetCurrentAsync(ShardId);

        Assert.AreEqual(CloudMarketplaceState.Enabled, configuration.State);
        Assert.AreEqual(CloudAggregateVersion.Initial, configuration.Version);
    }

    [TestMethod]
    public async Task SetState_ByAnAdmin_Succeeds_AndPersistsAcrossASimulatedRestart()
    {
        await using (var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var boundary = new CloudMarketplaceConfigurationBoundary(context);
            var initial = await boundary.GetCurrentAsync(ShardId);

            var outcome = await boundary.SetStateAsync(ShardId, CloudMarketplaceState.Disabled, AdminAccessLevel, initial.Version.Value);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        }

        await using var restarted = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var restartedBoundary = new CloudMarketplaceConfigurationBoundary(restarted);
        var configuration = await restartedBoundary.GetCurrentAsync(ShardId);

        Assert.AreEqual(CloudMarketplaceState.Disabled, configuration.State);
    }

    [TestMethod]
    public async Task SetState_ByANonAdmin_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var outcome = await boundary.SetStateAsync(ShardId, CloudMarketplaceState.Disabled, NonAdminAccessLevel, initial.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task WhileMaintenanceFrozen_OwnershipTransfer_IsRefused_ProvingMarketplaceFreezeAloneAlsoBlocksTheRealGate()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        var marketplaceBoundary = NewBoundary(out var marketplaceContext);
        await using var _ = marketplaceContext;
        var initial = await marketplaceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await marketplaceBoundary.SetStateAsync(ShardId, CloudMarketplaceState.MaintenanceFrozen, AdminAccessLevel, initial.Version.Value)).Kind);

        var transferAuthority = new CloudOwnershipTransferAuthority(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var transferOutcome = await transferAuthority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, transferOutcome.Kind);
        StringAssert.Contains(transferOutcome.Reason, "frozen");
    }

    [TestMethod]
    public async Task WhileDisabled_NotMaintenanceFrozen_OwnershipTransfer_IsNotBlockedByTheGate()
    {
        // MKT-203: Disabled blocks only new listings; it must not freeze ordinary custody mutations.
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        var marketplaceBoundary = NewBoundary(out var marketplaceContext);
        await using var _ = marketplaceContext;
        var initial = await marketplaceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await marketplaceBoundary.SetStateAsync(ShardId, CloudMarketplaceState.Disabled, AdminAccessLevel, initial.Version.Value)).Kind);

        var transferAuthority = new CloudOwnershipTransferAuthority(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var transferOutcome = await transferAuthority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, transferOutcome.Kind);
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
