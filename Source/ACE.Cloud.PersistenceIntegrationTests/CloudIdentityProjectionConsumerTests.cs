using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// AC Cloud Mule issue #22's Red -> Green coverage for
/// <see cref="CloudIdentityProjectionConsumer"/>, mirroring
/// <see cref="CloudCustodyProjectionConsumerTests"/> for the identity/allegiance outbox
/// (AUTH-003, VAULT-001, ARCH-007). This consumer never publishes to the Live State Stream (see its
/// doc comment), so these tests focus on the checkpoint/dead-letter/rebuild behavior it does own.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudIdentityProjectionConsumerTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextCharacterId = 0x80000000;

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
    public async Task RunBatchAsync_AppliesCharacterAndAllegianceEvents_IntoTheIdentityReadProjection()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId: 1, "Aluvia", totalLogins: 5, Guid.NewGuid());
        }

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceSworn, characterId, monarchId: 999, priorMonarchId: null, Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        var consumer = new CloudIdentityProjectionConsumer(consumerContext);
        var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(2, outcome.Value!.EventsApplied);

        await using var verifyContext = new CloudDbContext(options);
        var row = await verifyContext.CloudCharacterIdentityReadProjections.SingleAsync(r => r.CharacterId == characterId);
        Assert.AreEqual("Aluvia", row.CharacterName);
        Assert.AreEqual(5, row.TotalLogins);
        Assert.AreEqual(999u, row.MonarchId);
    }

    [TestMethod]
    public async Task RunBatchAsync_PoisonEvent_IsDeadLettered_AndDoesNotBlockLaterEvents()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var poisonedCharacterId = NextCharacterId();
        var healthyCharacterId = NextCharacterId();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, poisonedCharacterId, accountId: 1, "First", totalLogins: 1, Guid.NewGuid());
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, healthyCharacterId, accountId: 2, "Second", totalLogins: 1, Guid.NewGuid());
        }

        await using var context2 = new CloudDbContext(options);
        var consumer = new CloudIdentityProjectionConsumer(context2);

        var outcome = await consumer.RunBatchAsync(
            shardId: ShardId,
            maxCount: 100,
            poisonInjector: (_, evt) => evt.CharacterId == poisonedCharacterId
                ? new InvalidOperationException("Simulated poison event for a Red test.")
                : null,
            cancellationToken: CancellationToken.None);

        Assert.AreEqual(1, outcome.Value!.EventsApplied);
        Assert.AreEqual(1, outcome.Value.EventsDeadLettered);

        await using var verifyContext = new CloudDbContext(options);
        Assert.IsFalse(await verifyContext.CloudCharacterIdentityReadProjections.AnyAsync(r => r.CharacterId == poisonedCharacterId));
        Assert.IsTrue(await verifyContext.CloudCharacterIdentityReadProjections.AnyAsync(r => r.CharacterId == healthyCharacterId));

        var deadLetter = await verifyContext.CloudProjectionDeadLetters.SingleAsync();
        Assert.AreEqual(CloudIdentityProjectionConsumer.ConsumerName, deadLetter.ConsumerName);
    }

    [TestMethod]
    public async Task RunBatchAsync_AfterCheckpointLoss_RedeliveryNeverRegressesTheProjection()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId: 1, "OldName", totalLogins: 1, Guid.NewGuid());
        }

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId: 1, "NewName", totalLogins: 2, Guid.NewGuid());
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudIdentityProjectionConsumer(context);
            await consumer.RunBatchAsync(ShardId, maxCount: 100);
        }

        await using (var connection = new MySqlConnection(_fixture.CloudConnectionString))
        {
            await connection.OpenAsync();
            await using var reset = connection.CreateCommand();
            reset.CommandText = "UPDATE CloudProjectionCheckpoint SET LastAppliedSequenceNumber = 0 WHERE ConsumerName = 'IdentityProjection';";
            await reset.ExecuteNonQueryAsync();
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudIdentityProjectionConsumer(context);
            var redeliveryOutcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);
            Assert.AreEqual(0, redeliveryOutcome.Value!.EventsApplied);
            Assert.AreEqual(2, redeliveryOutcome.Value.EventsSkippedAsStale);
        }

        await using var verifyContext = new CloudDbContext(options);
        var row = await verifyContext.CloudCharacterIdentityReadProjections.SingleAsync(r => r.CharacterId == characterId);
        Assert.AreEqual("NewName", row.CharacterName);
    }

    [TestMethod]
    public async Task RebuildAsync_FromEmptyProjection_ProducesTheSameStateAsOrdinaryIncrementalConsumption()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterIds = new[] { NextCharacterId(), NextCharacterId() };

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            foreach (var characterId in characterIds)
            {
                await gateway.PublishCharacterIdentityEventAsync(
                    ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId: 1, $"Name{characterId}", totalLogins: 1, Guid.NewGuid());
            }
        }

        await using (var incrementalContext = new CloudDbContext(options))
        {
            var incrementalConsumer = new CloudIdentityProjectionConsumer(incrementalContext);
            await incrementalConsumer.RunBatchAsync(ShardId, maxCount: 100);
        }

        List<(uint CharacterId, string? CharacterName, long LastAppliedSequenceNumber)> incrementalSnapshot;
        await using (var snapshotContext = new CloudDbContext(options))
        {
            incrementalSnapshot = await snapshotContext.CloudCharacterIdentityReadProjections
                .OrderBy(r => r.CharacterId)
                .Select(r => new ValueTuple<uint, string?, long>(r.CharacterId, r.CharacterName, r.LastAppliedSequenceNumber))
                .ToListAsync();
        }

        await using (var rebuildContext = new CloudDbContext(options))
        {
            var rebuildConsumer = new CloudIdentityProjectionConsumer(rebuildContext);
            var rebuildOutcome = await rebuildConsumer.RebuildAsync(ShardId, batchSize: 1);
            Assert.AreEqual(2, rebuildOutcome.Value!.EventsApplied);
        }

        List<(uint CharacterId, string? CharacterName, long LastAppliedSequenceNumber)> rebuiltSnapshot;
        await using (var snapshotContext = new CloudDbContext(options))
        {
            rebuiltSnapshot = await snapshotContext.CloudCharacterIdentityReadProjections
                .OrderBy(r => r.CharacterId)
                .Select(r => new ValueTuple<uint, string?, long>(r.CharacterId, r.CharacterName, r.LastAppliedSequenceNumber))
                .ToListAsync();
        }

        CollectionAssert.AreEqual(incrementalSnapshot, rebuiltSnapshot);
    }

    [TestMethod]
    public async Task RunBatchAsync_AgainstAnUnreachableDatabase_ReturnsUnavailable_InsteadOfThrowing()
    {
        var options = CloudDbContextOptionsFactory.Create(UnreachableConnectionString(), await RealServerVersionAsync());
        await using var context = new CloudDbContext(options);
        var consumer = new CloudIdentityProjectionConsumer(context);

        var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Unavailable, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "unavailable");
    }

    private static uint NextCharacterId() => Interlocked.Increment(ref _nextCharacterId);

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
