using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #11's ordered, replayable Custody Outbox (ARCH-007) and this
/// acceptance criterion: "Deposits can commit with the web stack stopped and appear in a
/// replayable outbox." Nothing in these tests runs a web process or worker at all -- that is the
/// point: <see cref="CloudCustodyBoundary"/> commits deposits/withdrawals using only its own
/// MariaDB connection, and <see cref="CloudCustodyOutboxReader"/> proves the resulting events are
/// durable, strictly ordered, and re-readable as many times as a consumer needs once it eventually
/// does come online.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodyOutboxReaderTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 800_000;

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
    public async Task Deposits_CommitWithNothingEverConsumingTheOutbox_AndRemainFullyReplayableAfterward()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaIds = new[] { NextId(), NextId(), NextId() };

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        foreach (var biotaId in biotaIds)
        {
            await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
            var outcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, "Every deposit must commit on its own, independent of any outbox consumer ever having run.");
        }

        await using var readerContext = new CloudDbContext(options);
        var reader = new CloudCustodyOutboxReader(readerContext);

        var events = await reader.ReadAfterAsync(afterSequenceNumber: 0, maxCount: 100);

        Assert.HasCount(3, events);
        CollectionAssert.AreEqual(biotaIds, events.Select(e => (uint)e.BiotaId).ToArray(), "Events must replay in exact commit order.");

        // Reading again from the same cursor is non-destructive: a consumer that crashed before
        // durably advancing its cursor must see the same events again, not lose them.
        var repeated = await reader.ReadAfterAsync(afterSequenceNumber: 0, maxCount: 100);
        CollectionAssert.AreEqual(events.Select(e => e.Id).ToArray(), repeated.Select(e => e.Id).ToArray());
    }

    [TestMethod]
    public async Task ReadAfterAsync_ResumesExactlyAfterAConsumersLastAppliedSequenceNumber()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var firstBiotaId = NextId();
        var secondBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(firstBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        await using var readerContext = new CloudDbContext(options);
        var reader = new CloudCustodyOutboxReader(readerContext);
        var firstBatch = await reader.ReadAfterAsync(0, 100);
        Assert.HasCount(1, firstBatch);
        var cursor = firstBatch[0].SequenceNumber;

        await boundary.DepositAsync(secondBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        var secondBatch = await reader.ReadAfterAsync(cursor, 100);

        Assert.HasCount(1, secondBatch);
        Assert.AreEqual(secondBiotaId, secondBatch[0].BiotaId);
    }

    [TestMethod]
    public async Task SequenceNumbers_AreUniqueAndStrictlyIncreasing_AcrossConcurrentDeposits()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaIds = Enumerable.Range(0, 8).Select(_ => NextId()).ToArray();

        foreach (var biotaId in biotaIds)
        {
            await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        }

        var tasks = biotaIds.Select(async biotaId =>
        {
            await using var context = new CloudDbContext(options);
            var boundary = new CloudCustodyBoundary(context);
            return await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        });

        var results = await Task.WhenAll(tasks);
        Assert.IsTrue(results.All(r => r.Kind == CloudBoundaryOutcomeKind.Committed));

        await using var readerContext = new CloudDbContext(options);
        var reader = new CloudCustodyOutboxReader(readerContext);
        var events = await reader.ReadAfterAsync(0, 100);

        var sequenceNumbers = events.Select(e => e.SequenceNumber).ToList();
        Assert.HasCount(sequenceNumbers.Count, sequenceNumbers.Distinct().ToList(), "No two events may share a sequence number.");
        CollectionAssert.AreEqual(sequenceNumbers.OrderBy(n => n).ToList(), sequenceNumbers, "ReadAfterAsync must already return events in sequence order.");
    }

    [TestMethod]
    public async Task GetLatestSequenceNumberAsync_ReflectsCommittedEvents_AndIsZeroWhenEmpty()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using var emptyReaderContext = new CloudDbContext(options);
        var emptyReader = new CloudCustodyOutboxReader(emptyReaderContext);
        Assert.AreEqual(0, await emptyReader.GetLatestSequenceNumberAsync());

        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        await using var readerContext = new CloudDbContext(options);
        var reader = new CloudCustodyOutboxReader(readerContext);
        var latest = await reader.GetLatestSequenceNumberAsync();

        Assert.AreEqual(1, latest);
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
