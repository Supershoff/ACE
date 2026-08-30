using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Entity.Enum;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #32's Red requirement against a real database: "Test text/property queries across all
/// preserved fields ... projection rebuild, and revoked authorization," "admin disablement," "rate
/// limits," and "cross-account/shard enumeration."
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudInventorySearchReaderTests
{
    private const string ShardId = "us1";
    private const uint AdminAccessLevel = 5;

    private static readonly CloudRateLimitResult Allowed = CloudRateLimitResult.Allowed();

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 940_000;

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
    public async Task SearchAsync_NameContains_IsCaseInsensitiveAndOwnerScoped()
    {
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor, value: null, burden: null);
        await SeedWholeItemAsync(owner, "Steel Shield", ItemType.Armor, value: null, burden: null);

        var response = await SearchAsync(CloudLiveStreamViewer.ForOwners([owner]), new CloudInventorySearchRequest { NameContains = "ivory" });

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, response.Kind);
        Assert.HasCount(1, response.Page!.Items);
        Assert.AreEqual("Ivory Buckler", response.Page.Items[0].Name);
    }

    [TestMethod]
    public async Task SearchAsync_UnrelatedViewer_SeesNothing_NoCrossAccountEnumeration()
    {
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor, value: null, burden: null);

        var response = await SearchAsync(
            CloudLiveStreamViewer.ForOwners([Guid.NewGuid()]), new CloudInventorySearchRequest { NameContains = "ivory" });

        Assert.IsEmpty(response.Page!.Items);
    }

    [TestMethod]
    public async Task SearchAsync_MismatchedShardId_ReturnsNothingEvenForAnAdmin_NoCrossShardEnumeration()
    {
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor, value: null, burden: null);

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventorySearchReader(context);

        var response = await reader.SearchAsync(
            "a-different-shard", CloudLiveStreamViewer.ForAdmin(), new CloudInventorySearchRequest(), Allowed);

        Assert.IsEmpty(response.Page!.Items);
    }

    [TestMethod]
    public async Task SearchAsync_ValueRange_FiltersByProperty()
    {
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Cheap Ring", ItemType.Jewelry, value: 10, burden: null);
        await SeedWholeItemAsync(owner, "Expensive Ring", ItemType.Jewelry, value: 9_999, burden: null);

        var response = await SearchAsync(CloudLiveStreamViewer.ForOwners([owner]), new CloudInventorySearchRequest { MinValue = 1_000 });

        Assert.HasCount(1, response.Page!.Items);
        Assert.AreEqual("Expensive Ring", response.Page.Items[0].Name);
    }

    [TestMethod]
    public async Task SearchAsync_RegexPattern_WhenRegexEnabled_ReturnsMatches()
    {
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor, value: null, burden: null);
        await SeedWholeItemAsync(owner, "Ivory Wand", ItemType.Caster, value: null, burden: null);

        var response = await SearchAsync(
            CloudLiveStreamViewer.ForOwners([owner]), new CloudInventorySearchRequest { RegexPattern = "^Ivory Buckler$" });

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, response.Kind);
        Assert.HasCount(1, response.Page!.Items);
        Assert.AreEqual("Ivory Buckler", response.Page.Items[0].Name);
    }

    [TestMethod]
    public async Task SearchAsync_RegexPattern_WhenAdminDisabled_ReturnsDisabled_ButPlainSearchStillWorks()
    {
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor, value: null, burden: null);

        await using (var configContext = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var configBoundary = new CloudSearchConfigurationBoundary(configContext);
            var initial = await configBoundary.GetCurrentAsync(ShardId);
            var outcome = await configBoundary.SetRegexSearchEnabledAsync(ShardId, requested: false, AdminAccessLevel, initial.Version.Value);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        }

        var regexResponse = await SearchAsync(
            CloudLiveStreamViewer.ForOwners([owner]), new CloudInventorySearchRequest { RegexPattern = "Ivory" });
        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Disabled, regexResponse.Kind);
        Assert.IsNull(regexResponse.Page);

        var plainResponse = await SearchAsync(
            CloudLiveStreamViewer.ForOwners([owner]), new CloudInventorySearchRequest { NameContains = "Ivory" });
        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.Matched, plainResponse.Kind);
        Assert.HasCount(1, plainResponse.Page!.Items);
    }

    [TestMethod]
    public async Task SearchAsync_RateLimited_ReturnsRateLimited_WithoutRunningTheQuery()
    {
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor, value: null, burden: null);

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventorySearchReader(context);

        var response = await reader.SearchAsync(
            ShardId,
            CloudLiveStreamViewer.ForOwners([owner]),
            new CloudInventorySearchRequest { NameContains = "Ivory" },
            CloudRateLimitResult.RateLimited(TimeSpan.FromSeconds(30)));

        Assert.AreEqual(CloudSafeRegexSearchOutcomeKind.RateLimited, response.Kind);
        Assert.IsNull(response.Page);
    }

    [TestMethod]
    public async Task SearchAsync_AfterAProjectionRebuild_ReturnsTheSameResultAsBeforeTheRebuild()
    {
        // Simulates issue #32's "Rebuild and incremental indexing produce equivalent results"
        // acceptance criterion: CloudInventoryItemPropertiesProjection.TryApply is idempotent and
        // order-tolerant (see its own doc comment), so re-deriving a row from scratch at a later
        // revision -- exactly what a rebuild consumer replaying from an empty read model would do --
        // must search identically to the original incrementally-applied row.
        var owner = Guid.NewGuid();
        var biotaId = await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor, value: 500, burden: 12);

        var beforeRebuild = await SearchAsync(CloudLiveStreamViewer.ForOwners([owner]), new CloudInventorySearchRequest { NameContains = "Ivory" });
        Assert.HasCount(1, beforeRebuild.Page!.Items);

        await using (var rebuildContext = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var gateway = new CloudInventoryItemPropertiesGateway(rebuildContext);
            var reapplied = await gateway.UpsertAsync(
                biotaId, ShardId, "Ivory Buckler", ItemType.Armor, WeenieType.Generic, value: 500, burden: 12, iconCacheKeyHex: null, revision: 2);
            Assert.IsTrue(reapplied, "A higher-revision rebuild write must apply.");
        }

        var afterRebuild = await SearchAsync(CloudLiveStreamViewer.ForOwners([owner]), new CloudInventorySearchRequest { NameContains = "Ivory" });

        Assert.HasCount(1, afterRebuild.Page!.Items);
        Assert.AreEqual(beforeRebuild.Page.Items[0].Name, afterRebuild.Page.Items[0].Name);
        Assert.AreEqual(beforeRebuild.Page.Items[0].Value, afterRebuild.Page.Items[0].Value);
        Assert.AreEqual(beforeRebuild.Page.Items[0].Burden, afterRebuild.Page.Items[0].Burden);
        Assert.AreEqual(beforeRebuild.Page.Items[0].ItemId, afterRebuild.Page.Items[0].ItemId);
    }

    private async Task<CloudInventorySearchResponse> SearchAsync(CloudLiveStreamViewer viewer, CloudInventorySearchRequest request)
    {
        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventorySearchReader(context);
        return await reader.SearchAsync(ShardId, viewer, request, Allowed);
    }

    private async Task<uint> SeedWholeItemAsync(Guid owner, string name, ItemType itemType, int? value, int? burden)
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var propertiesContext = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var gateway = new CloudInventoryItemPropertiesGateway(propertiesContext);
            await gateway.UpsertAsync(biotaId, ShardId, name, itemType, WeenieType.Generic, value, burden, iconCacheKeyHex: null, revision: 1);
        }

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        context.CloudCustodyRecords.Add(new CloudCustodyRecord(biotaId, ShardId, owner, Guid.NewGuid()));
        await context.SaveChangesAsync();

        return biotaId;
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
