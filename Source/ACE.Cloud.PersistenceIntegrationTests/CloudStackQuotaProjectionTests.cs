using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #5's quota projection requirement (INV-004): "each independently
/// materializable lot counts once while no GUID is allocated early." <see cref="CloudStackQuotaProjection"/>
/// must count every Cloud Stack Lot an owner holds -- one stackable biota, unsplit, counts as one
/// item, and each additional lot produced by a split counts as one more projected item -- purely
/// from CloudStackLot rows, without ever materializing a native child biota or consuming a GUID.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStackQuotaProjectionTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 1_100_000;

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
    public async Task UnsplitStack_CountsAsExactlyOneProjectedItem()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 25, Guid.NewGuid());

        var projectedCount = await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, ownerId);

        Assert.AreEqual(1, projectedCount, "One stackable biota, unsplit, counts as exactly one item.");
    }

    [TestMethod]
    public async Task EachAdditionalSplitLot_CountsAsOneMoreProjectedItem_ForItsOwner()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var originalOwnerId = Guid.NewGuid();
        var secondOwnerId = Guid.NewGuid();
        var thirdOwnerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var authority = new CloudStackLotTransactionAuthority(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, originalOwnerId, 30, Guid.NewGuid());

        Assert.AreEqual(1, await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, originalOwnerId));

        var firstSplit = await authority.SplitLotAsync(depositOutcome.Value!.Lot.Id, depositOutcome.Value!.Lot.Version, secondOwnerId, 10);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, firstSplit.Kind);

        Assert.AreEqual(1, await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, originalOwnerId), "The original owner still holds exactly one lot.");
        Assert.AreEqual(1, await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, secondOwnerId), "The new owner's split-off lot counts as one projected item for them.");

        var secondSplit = await authority.SplitLotAsync(firstSplit.Value!.RemainingLot.Id, firstSplit.Value!.RemainingLot.Version, thirdOwnerId, 5);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, secondSplit.Kind);

        Assert.AreEqual(1, await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, originalOwnerId));
        Assert.AreEqual(1, await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, secondOwnerId));
        Assert.AreEqual(1, await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, thirdOwnerId));

        // No native GUID was ever allocated to reach this count: only the original biota exists.
        Assert.AreEqual(0, await context.CloudStackLotLineageEvents.CountAsync());
    }

    [TestMethod]
    public async Task MergingLotsBackTogether_ReducesTheProjectedCount()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var authority = new CloudStackLotTransactionAuthority(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 30, Guid.NewGuid());
        var splitOutcome = await authority.SplitLotAsync(depositOutcome.Value!.Lot.Id, depositOutcome.Value!.Lot.Version, ownerId, 10);

        Assert.AreEqual(2, await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, ownerId), "Splitting to the same owner still produces two separate lots (no auto-merge, ARCH-011).");

        var mergeOutcome = await authority.MergeLotsAsync(
            splitOutcome.Value!.RemainingLot.Id, splitOutcome.Value!.RemainingLot.Version,
            splitOutcome.Value!.NewLot.Id, splitOutcome.Value!.NewLot.Version);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, mergeOutcome.Kind);

        Assert.AreEqual(1, await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, ownerId));
    }

    [TestMethod]
    public async Task NonStackCustodyRecord_AlsoCountsAsOneProjectedItem_AlongsideStackLots()
    {
        var nonStackBiotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, nonStackBiotaId);

        var stackBiotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(nonStackBiotaId, ShardId, ownerId, Guid.NewGuid());
        await boundary.DepositStackAsync(stackBiotaId, ShardId, ownerId, 12, Guid.NewGuid());

        var projectedCount = await CloudStackQuotaProjection.CountProjectedItemsAsync(context, ShardId, ownerId);

        Assert.AreEqual(2, projectedCount, "A non-stack Cloud Item and an unsplit stack together count as two items.");
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
