using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #34's Red -> Green coverage for <see cref="CloudActivityLedgerQueryReader"/> against a real
/// MariaDB: owner/admin scoping across the custody-boundary and admin-only ledger tables, and
/// pagination.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudActivityLedgerQueryReaderTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 890_000;

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
    public async Task QueryAsync_OwnerScope_SeesOnlyItsOwnCustodyBoundaryEntries()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var owner = Guid.NewGuid();
        var biotaId = NextId();
        var otherBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, otherBiotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            await boundary.DepositAsync(biotaId, ShardId, owner, Guid.NewGuid());
            await boundary.DepositAsync(otherBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        }

        var reader = new CloudActivityLedgerQueryReader(new CloudDbContext(options));
        var page = await reader.QueryAsync(ShardId, CloudLiveStreamViewer.ForOwners([owner]), pageNumber: 1, pageSize: 10);

        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual(owner, page.Entries.Single().OwnerId);
        Assert.AreEqual(CloudActivityLedgerCategory.CustodyBoundary, page.Entries.Single().Category);
    }

    [TestMethod]
    public async Task QueryAsync_AdminViewer_SeesEveryOwnersEntries()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var firstBiotaId = NextId();
        var secondBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            await boundary.DepositAsync(firstBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            await boundary.DepositAsync(secondBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        }

        var reader = new CloudActivityLedgerQueryReader(new CloudDbContext(options));
        var page = await reader.QueryAsync(ShardId, CloudLiveStreamViewer.ForAdmin(), pageNumber: 1, pageSize: 10);

        Assert.AreEqual(2, page.TotalCount);
    }

    [TestMethod]
    public async Task QueryAsync_Pagination_SplitsResultsAcrossPages()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var owner = Guid.NewGuid();

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            for (var i = 0; i < 3; i++)
            {
                var biotaId = NextId();
                await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
                await boundary.DepositAsync(biotaId, ShardId, owner, Guid.NewGuid());
            }
        }

        var reader = new CloudActivityLedgerQueryReader(new CloudDbContext(options));

        var firstPage = await reader.QueryAsync(ShardId, CloudLiveStreamViewer.ForOwners([owner]), pageNumber: 1, pageSize: 2);
        Assert.AreEqual(3, firstPage.TotalCount);
        Assert.AreEqual(2, firstPage.TotalPages);
        Assert.HasCount(2, firstPage.Entries);

        var secondPage = await reader.QueryAsync(ShardId, CloudLiveStreamViewer.ForOwners([owner]), pageNumber: 2, pageSize: 2);
        Assert.HasCount(1, secondPage.Entries);
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
