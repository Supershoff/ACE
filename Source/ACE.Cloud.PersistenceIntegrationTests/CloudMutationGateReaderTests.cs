using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for <see cref="CloudMutationGateReader"/>, the shared commit-time gate
/// resolution issue #23 introduces to replace every hardcoded <see cref="CloudMutationGateState.Open"/>
/// Cloud Transaction Authority call site (see that enum's own doc comment).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudMutationGateReaderTests
{
    private const string ShardId = "us1";
    private const uint AdminAccessLevel = 5;
    private const uint AdminAccountId = 1;

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

    private CloudDbContext NewContext() => new(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));

    [TestMethod]
    public async Task ResolveAsync_WithNeitherAggregateBootstrapped_ReturnsOpen()
    {
        await using var context = NewContext();

        var gate = await CloudMutationGateReader.ResolveAsync(context, ShardId);

        Assert.AreEqual(CloudMutationGateState.Open, gate);
    }

    [TestMethod]
    public async Task ResolveAsync_AfterGlobalCloudMaintenanceEnters_ReturnsFrozen()
    {
        await using var context = NewContext();
        var maintenanceBoundary = new CloudGlobalMaintenanceBoundary(context);
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var gate = await CloudMutationGateReader.ResolveAsync(context, ShardId);

        Assert.AreEqual(CloudMutationGateState.Frozen, gate);
    }

    [TestMethod]
    public async Task ResolveAsync_AfterMarketplaceStateBecomesMaintenanceFrozen_ReturnsFrozen()
    {
        await using var context = NewContext();
        var marketplaceBoundary = new CloudMarketplaceConfigurationBoundary(context);
        var initial = await marketplaceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await marketplaceBoundary.SetStateAsync(ShardId, CloudMarketplaceState.MaintenanceFrozen, AdminAccessLevel, initial.Version.Value)).Kind);

        var gate = await CloudMutationGateReader.ResolveAsync(context, ShardId);

        Assert.AreEqual(CloudMutationGateState.Frozen, gate);
    }

    [TestMethod]
    public async Task ResolveAsync_AfterMarketplaceStateBecomesDisabled_ReturnsOpen_DisabledAloneNeverFreezesCustody()
    {
        await using var context = NewContext();
        var marketplaceBoundary = new CloudMarketplaceConfigurationBoundary(context);
        var initial = await marketplaceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await marketplaceBoundary.SetStateAsync(ShardId, CloudMarketplaceState.Disabled, AdminAccessLevel, initial.Version.Value)).Kind);

        var gate = await CloudMutationGateReader.ResolveAsync(context, ShardId);

        Assert.AreEqual(CloudMutationGateState.Open, gate);
    }

    [TestMethod]
    public async Task ResolveAsync_AfterGlobalCloudMaintenanceExits_ReturnsOpenAgain()
    {
        await using var context = NewContext();
        var maintenanceBoundary = new CloudGlobalMaintenanceBoundary(context);
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        var entered = await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, entered.Kind);

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.ExitAsync(ShardId, confirmed: true, AdminAccessLevel, entered.Value!.Version.Value)).Kind);

        var gate = await CloudMutationGateReader.ResolveAsync(context, ShardId);

        Assert.AreEqual(CloudMutationGateState.Open, gate);
    }
}
