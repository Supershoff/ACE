using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #39's Allegiance Vault Acting Character selector reader (VAULT-001): lists only the caller's
/// own current characters, with their last-known monarch from the versioned identity/allegiance
/// cache, and never another account's characters.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudActingCharacterReaderTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 700_000;

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
    public async Task GetCurrentCharactersAsync_ReturnsOnlyThisAccountsCharacters_WithTheirCachedMonarch()
    {
        var accountId = NextId();
        var otherAccountId = NextId();
        var characterId = NextId();
        var otherAccountsCharacterId = NextId();
        var monarchId = NextId();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId, "Vassal", totalLogins: 1, Guid.NewGuid());
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceSworn, characterId, monarchId, priorMonarchId: null,
                accountId, "Vassal", totalLogins: 1, Guid.NewGuid());
            await gateway.PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, otherAccountsCharacterId, otherAccountId, "Stranger", totalLogins: 1, Guid.NewGuid());
        }

        await using (var consumerContext = new CloudDbContext(options))
        {
            await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        }

        await using var readContext = new CloudDbContext(options);
        var reader = new CloudActingCharacterReader(readContext);

        var characters = await reader.GetCurrentCharactersAsync(ShardId, accountId);

        Assert.HasCount(1, characters);
        Assert.AreEqual(characterId, characters[0].CharacterId);
        Assert.AreEqual("Vassal", characters[0].CharacterName);
        Assert.AreEqual(monarchId, characters[0].MonarchId);
    }

    [TestMethod]
    public async Task GetCurrentCharactersAsync_ACharacterWithNoAllegiance_HasANullMonarch()
    {
        var accountId = NextId();
        var characterId = NextId();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var context = new CloudDbContext(options))
        {
            await new CloudIdentityEventGateway(context).PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId, "Solo", totalLogins: 1, Guid.NewGuid());
        }

        await using (var consumerContext = new CloudDbContext(options))
        {
            await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        }

        await using var readContext = new CloudDbContext(options);
        var characters = await new CloudActingCharacterReader(readContext).GetCurrentCharactersAsync(ShardId, accountId);

        Assert.HasCount(1, characters);
        Assert.IsNull(characters[0].MonarchId);
    }

    /// <summary>
    /// Issue #39's blocking human-acceptance regression: in a fresh/rebuilt Cloud database, the very
    /// first identity event for a character can be AllegianceSworn with no prior CharacterRenamed. The
    /// oath-first fix carries the account/name snapshot on the allegiance event itself, so the Acting
    /// Character selector must still see it for the authenticated account -- not only for accounts that
    /// happen to already have a rename/login event recorded.
    /// </summary>
    [TestMethod]
    public async Task GetCurrentCharactersAsync_OathFirst_WithNoPriorCharacterEvent_StillReturnsTheAccountAssociatedCharacter()
    {
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceSworn, characterId, monarchId, priorMonarchId: null,
                accountId, "OathFirst", totalLogins: 1, Guid.NewGuid());
        }

        await using (var consumerContext = new CloudDbContext(options))
        {
            await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        }

        await using var readContext = new CloudDbContext(options);
        var characters = await new CloudActingCharacterReader(readContext).GetCurrentCharactersAsync(ShardId, accountId);

        Assert.HasCount(1, characters);
        Assert.AreEqual(characterId, characters[0].CharacterId);
        Assert.AreEqual("OathFirst", characters[0].CharacterName);
        Assert.AreEqual(monarchId, characters[0].MonarchId);
    }

    /// <summary>
    /// Issue #39: a monarch change following an oath-first swear (no prior character event at all)
    /// must still resolve to the newest monarch, proving replay order -- not merely event presence --
    /// drives the final visible state.
    /// </summary>
    [TestMethod]
    public async Task GetCurrentCharactersAsync_OathFirst_ThenMonarchChanged_ReflectsTheNewestMonarch()
    {
        var accountId = NextId();
        var characterId = NextId();
        var firstMonarchId = NextId();
        var secondMonarchId = NextId();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceSworn, characterId, firstMonarchId, priorMonarchId: null,
                accountId, "Wanderer", totalLogins: 1, Guid.NewGuid());
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceMonarchChanged, characterId, secondMonarchId, priorMonarchId: firstMonarchId,
                accountId, "Wanderer", totalLogins: 1, Guid.NewGuid());
        }

        await using (var consumerContext = new CloudDbContext(options))
        {
            await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        }

        await using var readContext = new CloudDbContext(options);
        var characters = await new CloudActingCharacterReader(readContext).GetCurrentCharactersAsync(ShardId, accountId);

        Assert.HasCount(1, characters);
        Assert.AreEqual(secondMonarchId, characters[0].MonarchId);
    }

    /// <summary>
    /// Issue #39: after an oath-first swear breaks away, the character remains visible to its own
    /// account (account association is not conditioned on holding an allegiance) with a null monarch.
    /// </summary>
    [TestMethod]
    public async Task GetCurrentCharactersAsync_OathFirst_ThenAllegianceBroken_RemainsVisibleWithNoMonarch()
    {
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceSworn, characterId, monarchId, priorMonarchId: null,
                accountId, "Breakaway", totalLogins: 1, Guid.NewGuid());
            await gateway.PublishAllegianceEventAsync(
                ShardId, CloudIdentityEventType.AllegianceBroken, characterId, monarchId: null, priorMonarchId: monarchId,
                accountId, "Breakaway", totalLogins: 1, Guid.NewGuid());
        }

        await using (var consumerContext = new CloudDbContext(options))
        {
            await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        }

        await using var readContext = new CloudDbContext(options);
        var characters = await new CloudActingCharacterReader(readContext).GetCurrentCharactersAsync(ShardId, accountId);

        Assert.HasCount(1, characters);
        Assert.IsNull(characters[0].MonarchId);
    }

    /// <summary>
    /// Issue #39's blocking upgrade-path regression (independent review): a retained Cloud database
    /// from before the oath-first fix can already contain an AllegianceSworn-derived projection row
    /// with a null account/name association, making the character invisible to its own account. The
    /// self-heal fix's character-login-observed snapshot -- the only Cloud event ordinary login
    /// publishes -- must repair this end to end: the Acting Character selector reports the character
    /// under its own account again, with its actual current monarch.
    /// </summary>
    [TestMethod]
    public async Task GetCurrentCharactersAsync_LegacyDegradedAllegianceRow_IsRepairedAndReportsTheCurrentMonarch()
    {
        var accountId = NextId();
        var characterId = NextId();
        var staleMonarchId = NextId();
        var currentMonarchId = NextId();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

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

            await using var bumpSequence = connection.CreateCommand();
            bumpSequence.CommandText = "UPDATE CloudIdentityOutboxSequence SET NextValue = 2 WHERE Id = 1;";
            await bumpSequence.ExecuteNonQueryAsync();
        }

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIdentityEventGateway(context);
            await gateway.PublishCharacterLoginObservedEventAsync(
                ShardId, characterId, currentMonarchId, accountId, characterName: "Repaired", totalLogins: 2, Guid.NewGuid());
        }

        await using (var consumerContext = new CloudDbContext(options))
        {
            await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        }

        await using var readContext = new CloudDbContext(options);
        var characters = await new CloudActingCharacterReader(readContext).GetCurrentCharactersAsync(ShardId, accountId);

        Assert.HasCount(1, characters, "The character must be visible to its own account again after the next login's self-heal observation.");
        Assert.AreEqual(characterId, characters[0].CharacterId);
        Assert.AreEqual("Repaired", characters[0].CharacterName);
        Assert.AreEqual(currentMonarchId, characters[0].MonarchId, "The repaired row must report the character's actual current monarch, not the stale cached one.");
    }

    /// <summary>
    /// Issue #39's self-heal fix: an unaffiliated character's login observation must report no monarch,
    /// exactly like every other event shape already proves for its own case.
    /// </summary>
    [TestMethod]
    public async Task GetCurrentCharactersAsync_CharacterLoginObserved_UnaffiliatedCharacter_ReportsNoMonarch()
    {
        var accountId = NextId();
        var characterId = NextId();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var context = new CloudDbContext(options))
        {
            await new CloudIdentityEventGateway(context).PublishCharacterLoginObservedEventAsync(
                ShardId, characterId, monarchId: null, accountId, characterName: "Solo", totalLogins: 1, Guid.NewGuid());
        }

        await using (var consumerContext = new CloudDbContext(options))
        {
            await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
        }

        await using var readContext = new CloudDbContext(options);
        var characters = await new CloudActingCharacterReader(readContext).GetCurrentCharactersAsync(ShardId, accountId);

        Assert.HasCount(1, characters);
        Assert.IsNull(characters[0].MonarchId);
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
