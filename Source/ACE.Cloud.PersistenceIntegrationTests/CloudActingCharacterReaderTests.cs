using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

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
            await gateway.PublishAllegianceEventAsync(ShardId, CloudIdentityEventType.AllegianceSworn, characterId, monarchId, priorMonarchId: null, Guid.NewGuid());
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

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
