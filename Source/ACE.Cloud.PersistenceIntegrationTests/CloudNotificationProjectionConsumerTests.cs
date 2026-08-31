using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #34's Red -> Green coverage for <see cref="CloudNotificationProjectionConsumer"/>:
/// notification creation, coalescing while unread, a fresh notification after read, duplicate
/// outbox delivery, and non-notification-worthy events never creating a row. Mirrors
/// <see cref="CloudCustodyProjectionConsumerTests"/>'s exact fixture shape.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudNotificationProjectionConsumerTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 870_000;

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
    public async Task RunBatchAsync_OwnershipTransfer_CreatesANotificationAndPublishesALiveStreamEvent()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextId();
        var recipient = Guid.NewGuid();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        int custodyRecordVersion;
        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var depositOutcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            custodyRecordVersion = depositOutcome.Value!.Version;
        }

        await using (var context = new CloudDbContext(options))
        {
            var authority = new CloudOwnershipTransferAuthority(context);
            await authority.TransferAsync(biotaId, recipient, custodyRecordVersion, Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        var consumer = new CloudNotificationProjectionConsumer(consumerContext);
        var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(1, outcome.Value!.EventsApplied);

        await using var verifyContext = new CloudDbContext(options);
        var notification = await verifyContext.CloudNotifications.SingleAsync();
        Assert.AreEqual(recipient, notification.OwnerId);
        Assert.AreEqual(CloudNotificationKind.OwnershipReceived, notification.Kind);
        Assert.AreEqual(1, notification.OccurrenceCount);
        Assert.IsFalse(notification.IsRead);

        var liveStreamEvent = await verifyContext.CloudLiveStreamEvents.SingleAsync();
        Assert.AreEqual("Notification", liveStreamEvent.EventKind);
        Assert.AreEqual(recipient, liveStreamEvent.ScopeOwnerId);
        Assert.IsFalse(liveStreamEvent.IsPublic);
    }

    [TestMethod]
    public async Task RunBatchAsync_ASelfInitiatedDeposit_NeverCreatesANotification()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        var consumer = new CloudNotificationProjectionConsumer(consumerContext);
        var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);

        Assert.AreEqual(1, outcome.Value!.EventsRead);
        Assert.AreEqual(0, outcome.Value.EventsApplied);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudNotifications.CountAsync());
        Assert.AreEqual(0, await verifyContext.CloudLiveStreamEvents.CountAsync());
    }

    [TestMethod]
    public async Task RunBatchAsync_TwoOwnershipTransfersToTheSameOwnerWhileUnread_CoalesceIntoOneNotification()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var firstBiotaId = NextId();
        var secondBiotaId = NextId();
        var recipient = Guid.NewGuid();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var authority = new CloudOwnershipTransferAuthority(context);

            var firstDeposit = await boundary.DepositAsync(firstBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            await authority.TransferAsync(firstBiotaId, recipient, firstDeposit.Value!.Version, Guid.NewGuid());

            var secondDeposit = await boundary.DepositAsync(secondBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            await authority.TransferAsync(secondBiotaId, recipient, secondDeposit.Value!.Version, Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        var consumer = new CloudNotificationProjectionConsumer(consumerContext);
        await consumer.RunBatchAsync(ShardId, maxCount: 100);

        await using var verifyContext = new CloudDbContext(options);
        var notification = await verifyContext.CloudNotifications.SingleAsync();
        Assert.AreEqual(2, notification.OccurrenceCount, "Two OwnershipTransfer events to the same still-unread owner must coalesce into one notification.");
        Assert.AreEqual(2, await verifyContext.CloudLiveStreamEvents.CountAsync(e => e.EventKind == "Notification"), "Each occurrence still publishes its own Live State Stream update.");
    }

    [TestMethod]
    public async Task RunBatchAsync_TwoConcurrentRunsRacingRepeatedEvents_NeverCreateDuplicateNotificationRowsForTheSameOwner()
    {
        // AC Cloud Mule review of PR #149 (issue #34), P1: a plain (non-locking) read of "the most
        // recent notification for this (ShardId, OwnerId, Kind)" let two concurrent consumer runs
        // both observe "nothing to coalesce into yet" from their own snapshot and each insert their
        // own row for the very first occurrence -- there being no existing row for either run to
        // naturally serialize behind is exactly what made the duplicate reachable (the coalescing
        // path's own UPDATE would always have serialized on an existing row regardless of this bug,
        // since InnoDB always serializes a write against an already-locked existing row on its own).
        // Two real consumer instances, each with their own connection, repeatedly race to apply the
        // same still-unapplied event concurrently; verified once at the end via a fresh context,
        // rather than per-race, because a redelivery-guard backlog can shift exactly which owner's
        // event a given race actually resolves without ever creating a duplicate.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var warmupContext = new CloudDbContext(options))
        {
            await new CloudNotificationProjectionConsumer(warmupContext).RunBatchAsync(ShardId, maxCount: 1);
        }

        for (var i = 0; i < 10; i++)
        {
            var biotaId = NextId();
            var recipient = Guid.NewGuid();
            await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

            await using (var context = new CloudDbContext(options))
            {
                var boundary = new CloudCustodyBoundary(context);
                var authority = new CloudOwnershipTransferAuthority(context);
                var deposit = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
                await authority.TransferAsync(biotaId, recipient, deposit.Value!.Version, Guid.NewGuid());
            }

            await using var firstContext = new CloudDbContext(options);
            await using var secondContext = new CloudDbContext(options);
            var firstConsumer = new CloudNotificationProjectionConsumer(firstContext);
            var secondConsumer = new CloudNotificationProjectionConsumer(secondContext);

            try
            {
                await Task.WhenAll(
                    firstConsumer.RunBatchAsync(ShardId, maxCount: 1),
                    secondConsumer.RunBatchAsync(ShardId, maxCount: 1));
            }
            catch (ArgumentOutOfRangeException)
            {
                // A separate, pre-existing checkpoint-advance race (CloudProjectionCheckpoint.Advance
                // rejecting a redelivered event's sequence number) can surface once this loop's
                // aggressive maxCount:1 racing builds up a backlog. That is not issue #34's P1 this
                // test targets and is not fixed here (AGENTS.md: keep each pull request scoped to one
                // issue) -- this test's own invariant is checked below against whatever the database
                // actually ended up holding, regardless of how any individual race resolved.
            }
        }

        await using var verifyContext = new CloudDbContext(options);
        var duplicateOwners = await verifyContext.CloudNotifications
            .GroupBy(n => new { n.OwnerId, n.Kind })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.OwnerId)
            .ToListAsync();
        Assert.IsEmpty(duplicateOwners, "Two consumer runs repeatedly racing the same still-unapplied events must never leave more than one notification row for the same (owner, kind).");
    }

    [TestMethod]
    public async Task RunBatchAsync_AfterTheCoalescedNotificationIsRead_TheNextTransferStartsAFreshNotification()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var firstBiotaId = NextId();
        var secondBiotaId = NextId();
        var recipient = Guid.NewGuid();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var authority = new CloudOwnershipTransferAuthority(context);
            var firstDeposit = await boundary.DepositAsync(firstBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            await authority.TransferAsync(firstBiotaId, recipient, firstDeposit.Value!.Version, Guid.NewGuid());
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudNotificationProjectionConsumer(context);
            await consumer.RunBatchAsync(ShardId, maxCount: 100);
        }

        await using (var context = new CloudDbContext(options))
        {
            var notification = await context.CloudNotifications.SingleAsync();
            var gateway = new CloudNotificationGateway(context);
            var marked = await gateway.TryMarkReadAsync(ShardId, CloudLiveStreamViewer.ForOwners([recipient]), notification.Id);
            Assert.IsTrue(marked);
        }

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var authority = new CloudOwnershipTransferAuthority(context);
            var secondDeposit = await boundary.DepositAsync(secondBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            await authority.TransferAsync(secondBiotaId, recipient, secondDeposit.Value!.Version, Guid.NewGuid());
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudNotificationProjectionConsumer(context);
            await consumer.RunBatchAsync(ShardId, maxCount: 100);
        }

        await using var verifyContext = new CloudDbContext(options);
        var notifications = await verifyContext.CloudNotifications.OrderBy(n => n.FirstOccurredAtUtc).ToListAsync();
        Assert.HasCount(2, notifications);
        Assert.IsTrue(notifications[0].IsRead);
        Assert.AreEqual(1, notifications[0].OccurrenceCount);
        Assert.IsFalse(notifications[1].IsRead);
        Assert.AreEqual(1, notifications[1].OccurrenceCount);
    }

    [TestMethod]
    public async Task RunBatchAsync_AfterCheckpointLoss_RedeliveryNeverDuplicatesTheNotificationCount()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextId();
        var recipient = Guid.NewGuid();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var authority = new CloudOwnershipTransferAuthority(context);
            var deposit = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            await authority.TransferAsync(biotaId, recipient, deposit.Value!.Version, Guid.NewGuid());
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudNotificationProjectionConsumer(context);
            var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);
            Assert.AreEqual(1, outcome.Value!.EventsApplied);
        }

        // Simulate checkpoint loss / a duplicate outbox redelivery (issue #34 Red: "duplicate outbox
        // delivery does not duplicate notifications").
        await using (var connection = new MySqlConnection(_fixture.CloudConnectionString))
        {
            await connection.OpenAsync();
            await using var reset = connection.CreateCommand();
            reset.CommandText = "UPDATE CloudProjectionCheckpoint SET LastAppliedSequenceNumber = 0 WHERE ConsumerName = 'NotificationProjection';";
            await reset.ExecuteNonQueryAsync();
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudNotificationProjectionConsumer(context);
            var redeliveryOutcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);
            Assert.AreEqual(0, redeliveryOutcome.Value!.EventsApplied, "A redelivered event must never re-apply.");
        }

        await using var verifyContext = new CloudDbContext(options);
        var notification = await verifyContext.CloudNotifications.SingleAsync();
        Assert.AreEqual(1, notification.OccurrenceCount, "Redelivery must never duplicate the coalesced count.");
        Assert.AreEqual(1, await verifyContext.CloudLiveStreamEvents.CountAsync(e => e.EventKind == "Notification"));
    }

    [TestMethod]
    public async Task RunBatchAsync_RedeliveryOfAnAlreadyReadEvent_NeverCreatesASpuriousNotification()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextId();
        var recipient = Guid.NewGuid();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var authority = new CloudOwnershipTransferAuthority(context);
            var deposit = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            await authority.TransferAsync(biotaId, recipient, deposit.Value!.Version, Guid.NewGuid());
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudNotificationProjectionConsumer(context);
            var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);
            Assert.AreEqual(1, outcome.Value!.EventsApplied);
        }

        long appliedSequenceNumber;
        await using (var context = new CloudDbContext(options))
        {
            var notification = await context.CloudNotifications.SingleAsync();
            var gateway = new CloudNotificationGateway(context);
            var marked = await gateway.TryMarkReadAsync(ShardId, CloudLiveStreamViewer.ForOwners([recipient]), notification.Id);
            Assert.IsTrue(marked);
            appliedSequenceNumber = notification.LatestSourceSequenceNumber;
        }

        // Simulate a lost/rewound checkpoint that redelivers this exact event *after* its
        // notification was already read (issue #34 Red: "duplicate events do not duplicate
        // notifications or regress unread state") -- the specific gap the checkpoint-loss test
        // above does not cover, because that test resets the checkpoint before the notification is
        // ever read.
        await using (var connection = new MySqlConnection(_fixture.CloudConnectionString))
        {
            await connection.OpenAsync();
            await using var reset = connection.CreateCommand();
            reset.CommandText =
                "UPDATE CloudProjectionCheckpoint SET LastAppliedSequenceNumber = @lastApplied WHERE ConsumerName = 'NotificationProjection';";
            reset.Parameters.AddWithValue("@lastApplied", appliedSequenceNumber - 1);
            await reset.ExecuteNonQueryAsync();
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudNotificationProjectionConsumer(context);
            var redeliveryOutcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);
            Assert.AreEqual(0, redeliveryOutcome.Value!.EventsApplied, "A redelivered already-read event must never re-apply.");
        }

        await using var verifyContext = new CloudDbContext(options);
        var notifications = await verifyContext.CloudNotifications.ToListAsync();
        Assert.HasCount(1, notifications, "Redelivery of an already-read event's own sequence number must never mint a new notification.");
        Assert.IsTrue(notifications[0].IsRead, "Redelivery must never regress a notification back to unread.");
        Assert.AreEqual(1, notifications[0].OccurrenceCount);
    }

    [TestMethod]
    public async Task RunBatchAsync_AgainstAnUnreachableDatabase_ReturnsUnavailable_InsteadOfThrowing()
    {
        var options = CloudDbContextOptionsFactory.Create(UnreachableConnectionString(), await RealServerVersionAsync());
        await using var context = new CloudDbContext(options);
        var consumer = new CloudNotificationProjectionConsumer(context);

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
