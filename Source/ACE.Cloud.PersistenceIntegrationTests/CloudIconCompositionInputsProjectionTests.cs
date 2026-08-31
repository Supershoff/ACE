using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -&gt; Green coverage for issue #34's follow-up human-acceptance correction:
/// <see cref="CloudIconCompositionInputsProjection"/> used to drop
/// <see cref="CloudIconCompositionInputs.ItemTypeBackgroundDid"/> and
/// <see cref="CloudIconCompositionInputs.UiEffectDids"/> entirely, so a background + base + overlay +
/// magical UiEffect composition never survived the round trip to the runtime icon composition worker
/// even once ACE.Server started resolving them. Proves both now survive a real MariaDB round trip
/// through <see cref="CloudIconCompositionInputsGateway"/>.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudIconCompositionInputsProjectionTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 1_200_000;

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

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);

    [TestMethod]
    public async Task UpsertThenTryGet_BackgroundBaseOverlayAndMagicalUiEffect_AllSurviveTheRoundTrip()
    {
        var biotaId = NextBiotaId();
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        var inputs = new CloudIconCompositionInputs
        {
            BaseIconDid = 0x06006C0A,
            OverlayDid = 0x06006C34,
            ItemTypeBackgroundDid = 0x060011D3,
            UiEffectDids = [0x06000777],
        };

        await using (var writeContext = new CloudDbContext(options))
        {
            var gateway = new CloudIconCompositionInputsGateway(writeContext);
            var applied = await gateway.UpsertAsync(biotaId, ShardId, inputs, revision: 1);
            Assert.IsTrue(applied);
        }

        await using var readContext = new CloudDbContext(options);
        var readGateway = new CloudIconCompositionInputsGateway(readContext);
        var roundTripped = await readGateway.TryGetAsync(biotaId, ShardId);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(0x06006C0Au, roundTripped.BaseIconDid);
        Assert.AreEqual(0x06006C34u, roundTripped.OverlayDid);
        Assert.AreEqual(0x060011D3u, roundTripped.ItemTypeBackgroundDid);
        CollectionAssert.AreEqual(new[] { 0x06000777u }, roundTripped.UiEffectDids.ToList());
    }

    [TestMethod]
    public async Task UpsertThenTryGet_NoBackgroundOrUiEffects_RoundTripsAsNullAndEmpty()
    {
        var biotaId = NextBiotaId();
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        var inputs = new CloudIconCompositionInputs { BaseIconDid = 0x06006C0A };

        await using (var writeContext = new CloudDbContext(options))
        {
            var gateway = new CloudIconCompositionInputsGateway(writeContext);
            await gateway.UpsertAsync(biotaId, ShardId, inputs, revision: 1);
        }

        await using var readContext = new CloudDbContext(options);
        var readGateway = new CloudIconCompositionInputsGateway(readContext);
        var roundTripped = await readGateway.TryGetAsync(biotaId, ShardId);

        Assert.IsNotNull(roundTripped);
        Assert.IsNull(roundTripped.ItemTypeBackgroundDid);
        Assert.HasCount(0, roundTripped.UiEffectDids);
    }

    [TestMethod]
    public async Task Upsert_LaterRevisionChangesBackgroundAndUiEffects_OverwritesThePriorValues()
    {
        var biotaId = NextBiotaId();
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var firstContext = new CloudDbContext(options))
        {
            var gateway = new CloudIconCompositionInputsGateway(firstContext);
            await gateway.UpsertAsync(
                biotaId, ShardId,
                new CloudIconCompositionInputs { BaseIconDid = 0x06006C0A, ItemTypeBackgroundDid = 0x06000001, UiEffectDids = [0x06000002] },
                revision: 1);
        }

        await using (var secondContext = new CloudDbContext(options))
        {
            var gateway = new CloudIconCompositionInputsGateway(secondContext);
            var applied = await gateway.UpsertAsync(
                biotaId, ShardId,
                new CloudIconCompositionInputs { BaseIconDid = 0x06006C0A, ItemTypeBackgroundDid = 0x06000099, UiEffectDids = [] },
                revision: 2);
            Assert.IsTrue(applied);
        }

        await using var readContext = new CloudDbContext(options);
        var readGateway = new CloudIconCompositionInputsGateway(readContext);
        var roundTripped = await readGateway.TryGetAsync(biotaId, ShardId);

        Assert.AreEqual(0x06000099u, roundTripped!.ItemTypeBackgroundDid);
        Assert.HasCount(0, roundTripped.UiEffectDids);
    }
}
