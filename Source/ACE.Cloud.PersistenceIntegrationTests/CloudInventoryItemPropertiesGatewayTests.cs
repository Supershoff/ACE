using ACE.Cloud.Persistence;
using ACE.Entity.Enum;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #31 Red -> Green coverage for the read side of <see cref="CloudInventoryItemPropertiesGateway"/>
/// the Full Cloud Appraisal endpoint needs to build a panel from the only player-facing fields this
/// Cloud schema currently captures (name, value, burden).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudInventoryItemPropertiesGatewayTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 950_000;

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

    [TestMethod]
    public async Task TryGetAsync_RowExists_ReturnsItsFields()
    {
        var biotaId = NextBiotaId();
        await using (var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var gateway = new CloudInventoryItemPropertiesGateway(context);
            await gateway.UpsertAsync(biotaId, ShardId, "Ivory Buckler", ItemType.Armor, WeenieType.Generic, value: 100, burden: 20, iconCacheKeyHex: null, revision: 1);
        }

        await using var readContext = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var readGateway = new CloudInventoryItemPropertiesGateway(readContext);
        var row = await readGateway.TryGetAsync(biotaId, ShardId);

        Assert.IsNotNull(row);
        Assert.AreEqual("Ivory Buckler", row!.Name);
        Assert.AreEqual(100, row.Value);
        Assert.AreEqual(20, row.Burden);
    }

    [TestMethod]
    public async Task TryGetAsync_NoRowForBiota_ReturnsNull()
    {
        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var gateway = new CloudInventoryItemPropertiesGateway(context);

        Assert.IsNull(await gateway.TryGetAsync(NextBiotaId(), ShardId));
    }

    [TestMethod]
    public async Task TryGetAsync_MismatchedShardId_ReturnsNull()
    {
        var biotaId = NextBiotaId();
        await using (var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var gateway = new CloudInventoryItemPropertiesGateway(context);
            await gateway.UpsertAsync(biotaId, ShardId, "Ivory Buckler", ItemType.Armor, WeenieType.Generic, value: null, burden: null, iconCacheKeyHex: null, revision: 1);
        }

        await using var readContext = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var readGateway = new CloudInventoryItemPropertiesGateway(readContext);

        Assert.IsNull(await readGateway.TryGetAsync(biotaId, "a-different-shard"));
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
