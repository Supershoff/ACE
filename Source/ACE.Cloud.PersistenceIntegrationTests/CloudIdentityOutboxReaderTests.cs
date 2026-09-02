using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #17's identity/allegiance outbox (AUTH-003, VAULT-001, ARCH-007):
/// character rename/deletion and allegiance swear/break/monarch-change events publish durably and
/// replay in strict commit order, the same "outbox catch-up after restart" guarantee
/// <see cref="CloudCustodyOutboxReaderTests"/> proves for custody handoffs, applied here to events
/// that have no native biota at all.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudIdentityOutboxReaderTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextCharacterId = 700_000;

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
    public async Task PublishCharacterIdentityEventAsync_ThenReadAfter_ReplaysTheEventInFull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();

        await using var context = new CloudDbContext(options);
        var gateway = new CloudIdentityEventGateway(context);

        var published = await gateway.PublishCharacterIdentityEventAsync(
            ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId: 42, "NewName", totalLogins: 7, Guid.NewGuid());

        await using var readerContext = new CloudDbContext(options);
        var reader = new CloudIdentityOutboxReader(readerContext);
        var events = await reader.ReadAfterAsync(0, 100);

        Assert.HasCount(1, events);
        var evt = events[0];
        Assert.AreEqual(published.Id, evt.Id);
        Assert.AreEqual(CloudIdentityEventType.CharacterRenamed, evt.EventType);
        Assert.AreEqual(characterId, evt.CharacterId);
        Assert.AreEqual(42u, evt.AccountId);
        Assert.AreEqual("NewName", evt.CharacterName);
        Assert.AreEqual(7, evt.TotalLogins);
        Assert.IsNull(evt.MonarchId);
    }

    [TestMethod]
    public async Task PublishAllegianceEventAsync_ThenReadAfter_ReplaysTheEventInFull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();
        var monarchId = NextCharacterId();

        await using var context = new CloudDbContext(options);
        var gateway = new CloudIdentityEventGateway(context);

        await gateway.PublishAllegianceEventAsync(
            ShardId, CloudIdentityEventType.AllegianceSworn, characterId, monarchId, priorMonarchId: null,
            accountId: 42, characterName: "Sworn", totalLogins: 3, Guid.NewGuid());

        await using var readerContext = new CloudDbContext(options);
        var reader = new CloudIdentityOutboxReader(readerContext);
        var events = await reader.ReadAfterAsync(0, 100);

        Assert.HasCount(1, events);
        var evt = events[0];
        Assert.AreEqual(CloudIdentityEventType.AllegianceSworn, evt.EventType);
        Assert.AreEqual(characterId, evt.CharacterId);
        Assert.AreEqual(monarchId, evt.MonarchId);
        Assert.IsNull(evt.PriorMonarchId);
        // Issue #39's oath-first fix: an allegiance event now also carries the authoritative
        // account/name snapshot, so it alone can produce an account-associated projection.
        Assert.AreEqual(42u, evt.AccountId);
        Assert.AreEqual("Sworn", evt.CharacterName);
        Assert.AreEqual(3, evt.TotalLogins);
    }

    /// <summary>
    /// Issue #39's self-heal fix: a character-login-observed snapshot event publishes and replays
    /// exactly like the other two event shapes, carrying the current monarch (not a "prior" one -- an
    /// observation has no delta) alongside the same account/name/login snapshot.
    /// </summary>
    [TestMethod]
    public async Task PublishCharacterLoginObservedEventAsync_ThenReadAfter_ReplaysTheEventInFull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();
        var monarchId = NextCharacterId();

        await using var context = new CloudDbContext(options);
        var gateway = new CloudIdentityEventGateway(context);

        await gateway.PublishCharacterLoginObservedEventAsync(
            ShardId, characterId, monarchId, accountId: 42, characterName: "Observed", totalLogins: 5, Guid.NewGuid());

        await using var readerContext = new CloudDbContext(options);
        var reader = new CloudIdentityOutboxReader(readerContext);
        var events = await reader.ReadAfterAsync(0, 100);

        Assert.HasCount(1, events);
        var evt = events[0];
        Assert.AreEqual(CloudIdentityEventType.CharacterLoginObserved, evt.EventType);
        Assert.AreEqual(characterId, evt.CharacterId);
        Assert.AreEqual(monarchId, evt.MonarchId);
        Assert.IsNull(evt.PriorMonarchId);
        Assert.AreEqual(42u, evt.AccountId);
        Assert.AreEqual("Observed", evt.CharacterName);
        Assert.AreEqual(5, evt.TotalLogins);
    }

    [TestMethod]
    public async Task SequenceNumbers_AreStrictlyIncreasing_AndIndependentOfTheCustodyOutbox()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextCharacterId();
        var characterId = NextCharacterId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var gateway = new CloudIdentityEventGateway(context);

        // A custody deposit assigns SequenceNumber 1 in the *custody* outbox; the identity outbox's
        // own sequence must start independently at 1, not observe or share that counter.
        await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        var first = await gateway.PublishCharacterIdentityEventAsync(
            ShardId, CloudIdentityEventType.CharacterDeleted, characterId, accountId: 1, "Name", 0, Guid.NewGuid());
        var second = await gateway.PublishCharacterIdentityEventAsync(
            ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId: 1, "Name2", 0, Guid.NewGuid());

        Assert.AreEqual(1, first.SequenceNumber);
        Assert.AreEqual(2, second.SequenceNumber);

        var custodyReader = new CloudCustodyOutboxReader(context);
        Assert.AreEqual(1, await custodyReader.GetLatestSequenceNumberAsync());
    }

    [TestMethod]
    public async Task ReadAfterAsync_ResumesExactlyAfterAConsumersLastAppliedSequenceNumber()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var firstCharacterId = NextCharacterId();
        var secondCharacterId = NextCharacterId();

        await using var context = new CloudDbContext(options);
        var gateway = new CloudIdentityEventGateway(context);
        await gateway.PublishCharacterIdentityEventAsync(
            ShardId, CloudIdentityEventType.CharacterDeleted, firstCharacterId, 1, "A", 0, Guid.NewGuid());

        await using var readerContext = new CloudDbContext(options);
        var reader = new CloudIdentityOutboxReader(readerContext);
        var firstBatch = await reader.ReadAfterAsync(0, 100);
        Assert.HasCount(1, firstBatch);
        var cursor = firstBatch[0].SequenceNumber;

        await gateway.PublishCharacterIdentityEventAsync(
            ShardId, CloudIdentityEventType.CharacterDeleted, secondCharacterId, 1, "B", 0, Guid.NewGuid());

        var secondBatch = await reader.ReadAfterAsync(cursor, 100);

        Assert.HasCount(1, secondBatch);
        Assert.AreEqual(secondCharacterId, secondBatch[0].CharacterId);
    }

    [TestMethod]
    public async Task GetLatestSequenceNumberAsync_IsZeroWhenEmpty_AndReflectsCommittedEvents()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using var emptyReaderContext = new CloudDbContext(options);
        var emptyReader = new CloudIdentityOutboxReader(emptyReaderContext);
        Assert.AreEqual(0, await emptyReader.GetLatestSequenceNumberAsync());

        await using var context = new CloudDbContext(options);
        var gateway = new CloudIdentityEventGateway(context);
        await gateway.PublishCharacterIdentityEventAsync(
            ShardId, CloudIdentityEventType.CharacterDeleted, NextCharacterId(), 1, "A", 0, Guid.NewGuid());

        await using var readerContext = new CloudDbContext(options);
        var reader = new CloudIdentityOutboxReader(readerContext);
        Assert.AreEqual(1, await reader.GetLatestSequenceNumberAsync());
    }

    /// <summary>
    /// "Outbox catch-up after restart" (issue #17's Red section): a brand-new CloudDbContext/reader
    /// instance -- standing in for a companion process that just restarted -- must see every event a
    /// prior, now-disposed context/gateway instance committed, exactly like
    /// <see cref="CloudCustodyOutboxReaderTests"/> proves for the custody outbox.
    /// </summary>
    [TestMethod]
    public async Task Events_SurviveAndRemainReplayable_AfterASimulatedRestart()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var characterId = NextCharacterId();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterDeleted, characterId, 1, "Gone", 3, Guid.NewGuid());
        }

        // "Restart": a fresh context/reader, as a companion process would create after restarting.
        await using var restartedContext = new CloudDbContext(options);
        var reader = new CloudIdentityOutboxReader(restartedContext);
        var events = await reader.ReadAfterAsync(0, 100);

        Assert.HasCount(1, events);
        Assert.AreEqual(characterId, events[0].CharacterId);
    }

    private static uint NextCharacterId() => Interlocked.Increment(ref _nextCharacterId);
}
