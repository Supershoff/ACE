using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// AC Cloud Mule issue #22's Red -> Green coverage for
/// <see cref="CloudCustodyProjectionConsumer"/>: consumer restart/checkpoint loss, poison events, a
/// clean rebuild matching incremental consumption, and MariaDB-unavailable behavior (ARCH-007,
/// ARCH-009). Duplicate/out-of-order projection-row idempotency itself is proven abstractly by
/// <see cref="PersistenceCustodyProjectionEventConsumptionInvariantSuiteTests"/>; this file proves
/// the real outbox-driven consumer built on top of that same guarantee.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodyProjectionConsumerTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 850_000;

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
    public async Task RunBatchAsync_AppliesDepositsIntoTheReadProjection_AndPublishesPrivateLiveStreamEvents()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var firstBiotaId = NextId();
        var secondBiotaId = NextId();

        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            await boundary.DepositAsync(firstBiotaId, ShardId, firstOwner, Guid.NewGuid());
            await boundary.DepositAsync(secondBiotaId, ShardId, secondOwner, Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        var consumer = new CloudCustodyProjectionConsumer(consumerContext);
        var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(2, outcome.Value!.EventsRead);
        Assert.AreEqual(2, outcome.Value.EventsApplied);
        Assert.AreEqual(0, outcome.Value.EventsSkippedAsStale);
        Assert.AreEqual(0, outcome.Value.EventsDeadLettered);

        await using var verifyContext = new CloudDbContext(options);
        var firstRow = await verifyContext.CloudInventoryReadProjections.SingleAsync(r => r.BiotaId == firstBiotaId);
        Assert.AreEqual(firstOwner, firstRow.OwnerId);
        Assert.AreEqual(CloudBoundaryOperationType.Deposit, firstRow.LastEventType);

        var liveStreamEvents = await verifyContext.CloudLiveStreamEvents.OrderBy(e => e.SequenceNumber).ToListAsync();
        Assert.HasCount(2, liveStreamEvents);
        Assert.IsTrue(liveStreamEvents.All(e => !e.IsPublic));
        Assert.AreEqual(firstOwner, liveStreamEvents[0].ScopeOwnerId);
        Assert.AreEqual(secondOwner, liveStreamEvents[1].ScopeOwnerId);
    }

    [TestMethod]
    public async Task RunBatchAsync_ConsumerRestart_ResumesExactlyAfterItsDurableCheckpoint()
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
        }

        // First "process instance": consumes the first deposit and exits (simulating a restart).
        await using (var firstInstanceContext = new CloudDbContext(options))
        {
            var firstInstance = new CloudCustodyProjectionConsumer(firstInstanceContext);
            var firstOutcome = await firstInstance.RunBatchAsync(ShardId, maxCount: 100);
            Assert.AreEqual(1, firstOutcome.Value!.EventsRead);
        }

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            await boundary.DepositAsync(secondBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        }

        // A brand-new consumer instance (fresh CloudDbContext, no in-memory state) must resume from
        // the durable checkpoint rather than redelivering the first deposit.
        await using var secondInstanceContext = new CloudDbContext(options);
        var secondInstance = new CloudCustodyProjectionConsumer(secondInstanceContext);
        var secondOutcome = await secondInstance.RunBatchAsync(ShardId, maxCount: 100);

        Assert.AreEqual(1, secondOutcome.Value!.EventsRead);
        Assert.AreEqual(1, secondOutcome.Value.EventsApplied);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(2, await verifyContext.CloudInventoryReadProjections.CountAsync());
        Assert.AreEqual(2, await verifyContext.CloudLiveStreamEvents.CountAsync());
    }

    [TestMethod]
    public async Task RunBatchAsync_AfterCheckpointLoss_RedeliveryNeverRegressesTheProjection_OrDuplicatesLiveStreamEvents()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextId();
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        Guid custodyRecordId;
        int custodyRecordVersion;
        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var depositOutcome = await boundary.DepositAsync(biotaId, ShardId, firstOwner, Guid.NewGuid());
            custodyRecordId = depositOutcome.Value!.Id;
            custodyRecordVersion = depositOutcome.Value.Version;
        }

        await using (var context = new CloudDbContext(options))
        {
            var authority = new CloudOwnershipTransferAuthority(context);
            await authority.TransferAsync(biotaId, secondOwner, custodyRecordVersion, Guid.NewGuid());
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudCustodyProjectionConsumer(context);
            var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);
            Assert.AreEqual(2, outcome.Value!.EventsApplied);
        }

        // Simulate checkpoint loss: an operator/redeploy wipes the durable cursor back to the start,
        // exactly like the checkpoint row never having existed.
        await using (var connection = new MySqlConnection(_fixture.CloudConnectionString))
        {
            await connection.OpenAsync();
            await using var reset = connection.CreateCommand();
            reset.CommandText = "UPDATE CloudProjectionCheckpoint SET LastAppliedSequenceNumber = 0 WHERE ConsumerName = 'CustodyProjection';";
            await reset.ExecuteNonQueryAsync();
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudCustodyProjectionConsumer(context);
            var redeliveryOutcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);

            Assert.AreEqual(2, redeliveryOutcome.Value!.EventsRead);
            Assert.AreEqual(0, redeliveryOutcome.Value.EventsApplied, "Both events were already applied; redelivery must be a no-op.");
            Assert.AreEqual(2, redeliveryOutcome.Value.EventsSkippedAsStale);
        }

        await using var verifyContext = new CloudDbContext(options);
        var row = await verifyContext.CloudInventoryReadProjections.SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(secondOwner, row.OwnerId, "The projection must still reflect the last real owner, not regress to the first.");
        Assert.AreEqual(2, await verifyContext.CloudLiveStreamEvents.CountAsync(), "A stale redelivery must never publish a duplicate Live State Stream entry.");
    }

    [TestMethod]
    public async Task RunBatchAsync_PoisonEvent_IsDeadLettered_AndDoesNotBlockLaterEvents()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var poisonedBiotaId = NextId();
        var healthyBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, poisonedBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, healthyBiotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            await boundary.DepositAsync(poisonedBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            await boundary.DepositAsync(healthyBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        }

        await using var context2 = new CloudDbContext(options);
        var consumer = new CloudCustodyProjectionConsumer(context2);

        var outcome = await consumer.RunBatchAsync(
            shardId: ShardId,
            maxCount: 100,
            poisonInjector: (_, evt) => evt.BiotaId == poisonedBiotaId
                ? new InvalidOperationException("Simulated poison event for a Red test.")
                : null,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(2, outcome.Value!.EventsRead);
        Assert.AreEqual(1, outcome.Value.EventsApplied);
        Assert.AreEqual(1, outcome.Value.EventsDeadLettered);

        await using var verifyContext = new CloudDbContext(options);
        Assert.IsFalse(await verifyContext.CloudInventoryReadProjections.AnyAsync(r => r.BiotaId == poisonedBiotaId));
        Assert.IsTrue(await verifyContext.CloudInventoryReadProjections.AnyAsync(r => r.BiotaId == healthyBiotaId), "A poison event must not block a later, unrelated event.");

        var deadLetter = await verifyContext.CloudProjectionDeadLetters.SingleAsync();
        Assert.AreEqual(CloudCustodyProjectionConsumer.ConsumerName, deadLetter.ConsumerName);
        Assert.IsFalse(string.IsNullOrWhiteSpace(deadLetter.Reason));

        var checkpoint = await verifyContext.CloudProjectionCheckpoints.SingleAsync(c => c.ConsumerName == CloudCustodyProjectionConsumer.ConsumerName);
        Assert.IsGreaterThanOrEqualTo(2, checkpoint.LastAppliedSequenceNumber, "The checkpoint must advance past a dead-lettered event so it never blocks the consumer forever.");
    }

    [TestMethod]
    public async Task RebuildAsync_FromEmptyProjection_ProducesTheSameStateAsOrdinaryIncrementalConsumption()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaIds = new[] { NextId(), NextId(), NextId() };
        var owners = biotaIds.Select(_ => Guid.NewGuid()).ToArray();

        foreach (var biotaId in biotaIds)
        {
            await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        }

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            for (var i = 0; i < biotaIds.Length; i++)
            {
                await boundary.DepositAsync(biotaIds[i], ShardId, owners[i], Guid.NewGuid());
            }
        }

        // Path 1: ordinary incremental consumption, one small batch at a time.
        await using (var incrementalContext = new CloudDbContext(options))
        {
            var incrementalConsumer = new CloudCustodyProjectionConsumer(incrementalContext);
            CloudProjectionRunSummary summary;
            do
            {
                var batchOutcome = await incrementalConsumer.RunBatchAsync(ShardId, maxCount: 1);
                summary = batchOutcome.Value!;
            }
            while (!summary.CaughtUp);
        }

        IReadOnlyList<(uint BiotaId, Guid OwnerId, long LastAppliedSequenceNumber)> incrementalSnapshot;
        await using (var snapshotContext = new CloudDbContext(options))
        {
            incrementalSnapshot = await snapshotContext.CloudInventoryReadProjections
                .OrderBy(r => r.BiotaId)
                .Select(r => new ValueTuple<uint, Guid, long>(r.BiotaId, r.OwnerId, r.LastAppliedSequenceNumber))
                .ToListAsync();
        }

        // Path 2: a full rebuild, starting from empty (issue #22's "empty rebuild" Red case).
        await using (var rebuildContext = new CloudDbContext(options))
        {
            var rebuildConsumer = new CloudCustodyProjectionConsumer(rebuildContext);
            var rebuildOutcome = await rebuildConsumer.RebuildAsync(ShardId, batchSize: 2);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, rebuildOutcome.Kind);
            Assert.AreEqual(3, rebuildOutcome.Value!.EventsApplied);
        }

        IReadOnlyList<(uint BiotaId, Guid OwnerId, long LastAppliedSequenceNumber)> rebuiltSnapshot;
        await using (var snapshotContext = new CloudDbContext(options))
        {
            rebuiltSnapshot = await snapshotContext.CloudInventoryReadProjections
                .OrderBy(r => r.BiotaId)
                .Select(r => new ValueTuple<uint, Guid, long>(r.BiotaId, r.OwnerId, r.LastAppliedSequenceNumber))
                .ToListAsync();
        }

        CollectionAssert.AreEqual(incrementalSnapshot.ToList(), rebuiltSnapshot.ToList());
    }

    [TestMethod]
    public async Task RunBatchAsync_AgainstAnUnreachableDatabase_ReturnsUnavailable_InsteadOfThrowing()
    {
        var options = CloudDbContextOptionsFactory.Create(UnreachableConnectionString(), await RealServerVersionAsync());
        await using var context = new CloudDbContext(options);
        var consumer = new CloudCustodyProjectionConsumer(context);

        var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Unavailable, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "unavailable");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private string UnreachableConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder(_fixture.CloudConnectionString)
        {
            Server = "127.0.0.1",
            Port = 1,
            ConnectionTimeout = 2,
        };

        return builder.ConnectionString;
    }

    private async Task<ServerVersion> RealServerVersionAsync() =>
        await Task.Run(() => ServerVersion.AutoDetect(_fixture.CloudConnectionString));
}
