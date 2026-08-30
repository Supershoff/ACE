using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #33's Red -> Green coverage for <see cref="CloudCharacterIdentityReader"/> (AUTH-003):
/// gathers Display Character candidates from the identity read projection
/// <see cref="CloudIdentityProjectionConsumerTests"/> already proves is populated correctly, scoped
/// to a specific set of account IDs.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCharacterIdentityReaderTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextCharacterId = 0x90000000;

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

    private static async Task SeedCharacterAsync(DbContextOptions<CloudDbContext> options, uint characterId, uint accountId, string name, int totalLogins)
    {
        await using (var context = new CloudDbContext(options))
        {
            await new CloudIdentityEventGateway(context).PublishCharacterIdentityEventAsync(
                ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId, name, totalLogins, Guid.NewGuid());
        }

        await using var consumerContext = new CloudDbContext(options);
        await new CloudIdentityProjectionConsumer(consumerContext).RunBatchAsync(ShardId, maxCount: 100);
    }

    [TestMethod]
    public async Task GetCandidatesAsync_ReturnsOnlyCharactersBelongingToTheRequestedAccounts()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        var inScopeCharacterId = NextCharacterId();
        var otherAccountCharacterId = NextCharacterId();
        await SeedCharacterAsync(options, inScopeCharacterId, accountId: 1, "Aluvia", totalLogins: 5);
        await SeedCharacterAsync(options, otherAccountCharacterId, accountId: 2, "SomeoneElse", totalLogins: 500);

        await using var context = new CloudDbContext(options);
        var candidates = await new CloudCharacterIdentityReader(context).GetCandidatesAsync(ShardId, [1u]);

        Assert.HasCount(1, candidates);
        Assert.AreEqual(inScopeCharacterId, candidates[0].CharacterId);
        Assert.AreEqual("Aluvia", candidates[0].CharacterName);
        Assert.AreEqual(5, candidates[0].TotalLogins);
    }

    [TestMethod]
    public async Task GetCandidatesAsync_MultipleAccountsInScope_ReturnsCharactersFromEachOne()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        var mainCharacterId = NextCharacterId();
        var linkedCharacterId = NextCharacterId();
        await SeedCharacterAsync(options, mainCharacterId, accountId: 10, "MainChar", totalLogins: 5);
        await SeedCharacterAsync(options, linkedCharacterId, accountId: 11, "LinkedChar", totalLogins: 500);

        await using var context = new CloudDbContext(options);
        var candidates = await new CloudCharacterIdentityReader(context).GetCandidatesAsync(ShardId, [10u, 11u]);

        CollectionAssert.AreEquivalent(
            new[] { mainCharacterId, linkedCharacterId }, candidates.Select(c => c.CharacterId).ToArray());
    }

    [TestMethod]
    public async Task GetCandidatesAsync_NoAccountsRequested_ReturnsAnEmptyList()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var candidates = await new CloudCharacterIdentityReader(context).GetCandidatesAsync(ShardId, []);

        Assert.HasCount(0, candidates);
    }

    private static uint NextCharacterId() => Interlocked.Increment(ref _nextCharacterId);
}
