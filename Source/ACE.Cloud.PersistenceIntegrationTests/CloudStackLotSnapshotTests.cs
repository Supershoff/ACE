using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #33's Red -> Green coverage for <see cref="CloudStackLotTransactionAuthority.TryGetLotSnapshotAsync"/>:
/// the plain ownership/quantity/version read the withdrawal-open endpoint uses to authorize a
/// partial-quantity split request server-side before calling <c>SplitLotAsync</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStackLotSnapshotTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 850_000;

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
    public async Task TryGetLotSnapshotAsync_ExistingLot_ReturnsItsOwnerQuantityAndVersion()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        Guid lotId;
        await using (var depositContext = new CloudDbContext(options))
        {
            var depositOutcome = await new CloudCustodyBoundary(depositContext)
                .DepositStackAsync(biotaId, ShardId, ownerId, quantity: 10, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
            lotId = depositOutcome.Value!.Lot.Id;
        }

        await using var context = new CloudDbContext(options);
        var snapshot = await new CloudStackLotTransactionAuthority(context).TryGetLotSnapshotAsync(lotId);

        Assert.IsNotNull(snapshot);
        Assert.AreEqual(ownerId, snapshot!.OwnerId);
        Assert.AreEqual(10, snapshot.Quantity);
        Assert.AreEqual(1, snapshot.Version);
    }

    [TestMethod]
    public async Task TryGetLotSnapshotAsync_NonexistentLot_ReturnsNull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var snapshot = await new CloudStackLotTransactionAuthority(context).TryGetLotSnapshotAsync(Guid.NewGuid());

        Assert.IsNull(snapshot);
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
