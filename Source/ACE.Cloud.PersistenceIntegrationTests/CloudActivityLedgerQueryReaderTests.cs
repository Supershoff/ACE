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

    [TestMethod]
    public async Task QueryAsync_OwnerScope_NeverTruncatesHistoryOrMisreportsTotalsBeyondTheOldFixedCandidateWindow()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var owner = Guid.NewGuid();

        // One more than the reader's old fixed 500-row candidate window (issue #34 code review
        // finding): a single owner accumulating this much history must never see it silently
        // truncated. Inserted directly against CloudActivityLedgerEvents -- bypassing
        // CloudCustodyBoundary/AceShardTestData entirely -- because the ledger table has no foreign
        // key into the (separate-database) ACE shard, and 501 real custody deposits would make this
        // test far slower without proving anything more about the reader itself.
        const int totalEvents = 501;
        await using (var context = new CloudDbContext(options))
        {
            for (var i = 0; i < totalEvents; i++)
            {
                context.CloudActivityLedgerEvents.Add(new CloudActivityLedgerEvent(
                    Guid.NewGuid(), ShardId, CloudBoundaryOperationType.Deposit, NextId(), owner, CloudBoundaryOutcomeKind.Committed));
            }

            await context.SaveChangesAsync();
        }

        var reader = new CloudActivityLedgerQueryReader(new CloudDbContext(options));

        var firstPage = await reader.QueryAsync(ShardId, CloudLiveStreamViewer.ForOwners([owner]), pageNumber: 1, pageSize: 10);
        Assert.AreEqual(totalEvents, firstPage.TotalCount, "A single owner's full ledger history must never be silently truncated by a fixed candidate window.");
        var expectedTotalPages = (totalEvents + 9) / 10;
        Assert.AreEqual(expectedTotalPages, firstPage.TotalPages);
        Assert.HasCount(10, firstPage.Entries);

        var lastPage = await reader.QueryAsync(ShardId, CloudLiveStreamViewer.ForOwners([owner]), pageNumber: expectedTotalPages, pageSize: 10);
        var expectedLastPageCount = totalEvents - (expectedTotalPages - 1) * 10;
        Assert.HasCount(expectedLastPageCount, lastPage.Entries, "The final page beyond the old 500-row window must still return real rows, not come back empty.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
