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
                ShardId, CloudIdentityEventType.AllegianceSworn, characterId, monarchId: 999, priorMonarchId: null,
                accountId: 1, characterName: "Aluvia", totalLogins: 5, Guid.NewGuid());
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

    /// <summary>
    /// Issue #39's blocking oath-first regression: in a fresh/rebuilt Cloud database, AllegianceSworn
    /// can be the very first identity event a character ever produces (no prior rename/login event).
    /// The resulting projection row must still carry the character's AccountId/CharacterName so
    /// <see cref="CloudActingCharacterReader"/> does not filter it out for its own account.
    /// </summary>
    [TestMethod]
    public async Task RunBatchAsync_OathFirstAllegianceEvent_WithNoPriorCharacterEvent_StillPopulatesAccountAndName()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();
        var monarchId = NextCharacterId();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceSworn, characterId, monarchId, priorMonarchId: null,
                accountId: 7, characterName: "OathFirst", totalLogins: 1, Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        var consumer = new CloudIdentityProjectionConsumer(consumerContext);
        var outcome = await consumer.RunBatchAsync(ShardId, maxCount: 100);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(1, outcome.Value!.EventsApplied);

        await using var verifyContext = new CloudDbContext(options);
        var row = await verifyContext.CloudCharacterIdentityReadProjections.SingleAsync(r => r.CharacterId == characterId);
        Assert.AreEqual(7u, row.AccountId);
        Assert.AreEqual("OathFirst", row.CharacterName);
        Assert.AreEqual(monarchId, row.MonarchId);
    }

    /// <summary>
    /// Issue #39: an AllegianceBroken event following an oath-first swear must clear the monarch
    /// pointer while the account/name snapshot -- carried on every allegiance event now, not only the
    /// first -- remains populated.
    /// </summary>
    [TestMethod]
    public async Task RunBatchAsync_AllegianceBrokenAfterOathFirstSwear_ClearsMonarch_ButKeepsAccountAssociation()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();
        var monarchId = NextCharacterId();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceSworn, characterId, monarchId, priorMonarchId: null,
                accountId: 9, characterName: "BreaksAway", totalLogins: 2, Guid.NewGuid());
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceBroken, characterId, monarchId: null, priorMonarchId: monarchId,
                accountId: 9, characterName: "BreaksAway", totalLogins: 2, Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        var outcome = await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        Assert.AreEqual(2, outcome.Value!.EventsApplied);

        await using var verifyContext = new CloudDbContext(options);
        var row = await verifyContext.CloudCharacterIdentityReadProjections.SingleAsync(r => r.CharacterId == characterId);
        Assert.AreEqual(9u, row.AccountId);
        Assert.AreEqual("BreaksAway", row.CharacterName);
        Assert.IsNull(row.MonarchId);
    }

    /// <summary>
    /// Issue #39's blocking upgrade-path regression (independent review): a retained Cloud database
    /// from before the oath-first fix can already contain an AllegianceSworn-derived projection row
    /// with a null account/name association -- this seeds exactly that degraded row directly (the
    /// oath-first fix's own event-shape validation now forbids ever producing one through the gateway
    /// again, so a raw insert is the only way to reproduce a row a pre-fix build actually left behind).
    /// A character-login-observed snapshot (issue #39's self-heal fix) is the only event ordinary login
    /// publishes, and must fully repair the row -- including replacing a stale cached monarch with the
    /// character's actual current one -- without any allegiance mutation happening first.
    /// </summary>
    [TestMethod]
    public async Task RunBatchAsync_LegacyDegradedAllegianceRow_IsRepairedByTheNextLoginObservation()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();
        var staleMonarchId = NextCharacterId();
        var currentMonarchId = NextCharacterId();

        await using (var connection = new MySqlConnection(_fixture.CloudConnectionString))
        {
            await connection.OpenAsync();

            await using var insertDegradedRow = connection.CreateCommand();
            insertDegradedRow.CommandText = """
                INSERT INTO CloudCharacterIdentityReadProjection
                    (CharacterId, ShardId, AccountId, CharacterName, TotalLogins, MonarchId, LastAppliedSequenceNumber)
                VALUES (@characterId, @shardId, NULL, NULL, NULL, @staleMonarchId, 1);
                """;
            insertDegradedRow.Parameters.AddWithValue("@characterId", characterId);
            insertDegradedRow.Parameters.AddWithValue("@shardId", ShardId);
            insertDegradedRow.Parameters.AddWithValue("@staleMonarchId", staleMonarchId);
            await insertDegradedRow.ExecuteNonQueryAsync();

            // The pre-fix row's own (never-replayed-here) AllegianceSworn event already occupies
            // sequence 1; the next real event this character produces -- their next login -- must
            // reserve a strictly higher sequence number for CloudProjectionSequenceGuard to apply it.
            await using var bumpSequence = connection.CreateCommand();
            bumpSequence.CommandText = "UPDATE CloudIdentityOutboxSequence SET NextValue = 2 WHERE Id = 1;";
            await bumpSequence.ExecuteNonQueryAsync();
        }

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterLoginObservedEventAsync(
                ShardId, characterId, currentMonarchId, accountId: 11, characterName: "Repaired", totalLogins: 4, Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        var outcome = await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.AreEqual(1, outcome.Value!.EventsApplied);

        await using var verifyContext = new CloudDbContext(options);
        var row = await verifyContext.CloudCharacterIdentityReadProjections.SingleAsync(r => r.CharacterId == characterId);
        Assert.AreEqual(11u, row.AccountId, "The login observation must repair the null AccountId a pre-oath-first-fix row was left with.");
        Assert.AreEqual("Repaired", row.CharacterName);
        Assert.AreEqual(4, row.TotalLogins);
        Assert.AreEqual(currentMonarchId, row.MonarchId, "The repair must reflect the character's actual current monarch, not the stale cached one.");
    }

    /// <summary>
    /// Issue #39's self-heal fix: an unaffiliated character's login observation carries a null monarch,
    /// and the projection must faithfully report that -- not merely leave a previous value untouched.
    /// </summary>
    [TestMethod]
    public async Task RunBatchAsync_CharacterLoginObserved_UnaffiliatedCharacter_LeavesMonarchNull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterLoginObservedEventAsync(
                ShardId, characterId, monarchId: null, accountId: 21, characterName: "Solo", totalLogins: 1, Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        var outcome = await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        Assert.AreEqual(1, outcome.Value!.EventsApplied);

        await using var verifyContext = new CloudDbContext(options);
        var row = await verifyContext.CloudCharacterIdentityReadProjections.SingleAsync(r => r.CharacterId == characterId);
        Assert.AreEqual(21u, row.AccountId);
        Assert.AreEqual("Solo", row.CharacterName);
        Assert.IsNull(row.MonarchId);
    }

    /// <summary>
    /// Issue #39: mirrors <see cref="RunBatchAsync_AfterCheckpointLoss_RedeliveryNeverRegressesTheProjection"/>
    /// for the new character-login-observed event type -- a checkpoint-loss redelivery of two logins
    /// (an older monarch observation, then a newer one) must skip both as stale and never regress the
    /// projection back to the older, already-superseded monarch.
    /// </summary>
    [TestMethod]
    public async Task RunBatchAsync_StaleCharacterLoginObservedRedelivery_NeverRegressesTheProjection()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();
        var firstMonarchId = NextCharacterId();
        var secondMonarchId = NextCharacterId();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterLoginObservedEventAsync(
                ShardId, characterId, firstMonarchId, accountId: 31, characterName: "Wanderer", totalLogins: 1, Guid.NewGuid());
        }

        await using (var context = new CloudDbContext(options))
        {
            var consumer = new CloudIdentityProjectionConsumer(context);
            await consumer.RunBatchAsync(ShardId, maxCount: 100);
        }

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterLoginObservedEventAsync(
                ShardId, characterId, secondMonarchId, accountId: 31, characterName: "Wanderer", totalLogins: 2, Guid.NewGuid());
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
        Assert.AreEqual(secondMonarchId, row.MonarchId, "A stale redelivery of an earlier login observation must never roll the monarch pointer backward.");
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
