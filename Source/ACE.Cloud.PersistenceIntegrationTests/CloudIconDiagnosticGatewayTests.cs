using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #26's deduplicated Icon Reconstruction diagnostics (UI-006: "create
/// an administrator-visible diagnostic"). Proves the (ShardId, DedupeKey) upsert is an enforced
/// database constraint, not only application logic: two calls reporting the same broken reference
/// converge on one row with an incremented count, and two different broken references never collide.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudIconDiagnosticGatewayTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;

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
    public async Task RecordAsync_ANewDiagnostic_InsertsOneRowWithOccurrenceCountOne()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gateway = new CloudIconDiagnosticGateway(context);
        var diagnostic = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.Overlay, 0x06000030), CloudIconLayerResolutionOutcomeKind.Missing);

        await gateway.RecordAsync(ShardId, diagnostic, DateTime.UtcNow);

        await using var verifyContext = new CloudDbContext(options);
        var row = await verifyContext.CloudIconDiagnostics.SingleAsync(d => d.ShardId == ShardId && d.DedupeKey == diagnostic.DedupeKey);
        Assert.AreEqual(1, row.OccurrenceCount);
        Assert.AreEqual(CloudIconLayerKind.Overlay, row.LayerKind);
        Assert.AreEqual(0x06000030u, row.Did);
        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Missing, row.Reason);
    }

    [TestMethod]
    public async Task RecordAsync_TheSameDiagnosticTwice_UpsertsIntoOneRowWithIncrementedCount()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var diagnostic = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000010), CloudIconLayerResolutionOutcomeKind.Corrupt);

        await using (var first = new CloudDbContext(options))
        {
            await new CloudIconDiagnosticGateway(first).RecordAsync(ShardId, diagnostic, DateTime.UtcNow);
        }

        await using (var second = new CloudDbContext(options))
        {
            await new CloudIconDiagnosticGateway(second).RecordAsync(ShardId, diagnostic, DateTime.UtcNow.AddMinutes(1));
        }

        await using var verifyContext = new CloudDbContext(options);
        var rows = await verifyContext.CloudIconDiagnostics.Where(d => d.ShardId == ShardId && d.DedupeKey == diagnostic.DedupeKey).ToListAsync();

        Assert.AreEqual(1, rows.Count, "Expected the two identical reports to upsert into a single deduplicated row.");
        Assert.AreEqual(2, rows[0].OccurrenceCount);
    }

    [TestMethod]
    public async Task RecordAsync_DifferentReasonsForTheSameLayer_ProduceSeparateRows()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var missing = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.Underlay, 0x06000020), CloudIconLayerResolutionOutcomeKind.Missing);
        var corrupt = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.Underlay, 0x06000020), CloudIconLayerResolutionOutcomeKind.Corrupt);

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIconDiagnosticGateway(context);
            await gateway.RecordAsync(ShardId, missing, DateTime.UtcNow);
            await gateway.RecordAsync(ShardId, corrupt, DateTime.UtcNow);
        }

        await using var verifyContext = new CloudDbContext(options);
        var rowCount = await verifyContext.CloudIconDiagnostics.CountAsync(d => d.ShardId == ShardId && d.Did == 0x06000020);

        Assert.AreEqual(2, rowCount);
    }
}
