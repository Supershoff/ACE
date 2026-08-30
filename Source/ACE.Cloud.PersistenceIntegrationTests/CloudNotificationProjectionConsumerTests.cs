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
