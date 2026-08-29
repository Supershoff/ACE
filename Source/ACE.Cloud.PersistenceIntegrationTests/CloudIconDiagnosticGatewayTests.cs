using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #26's deduplicated Icon Reconstruction diagnostics (UI-006: "create
/// an administrator-visible diagnostic"). Proves the (ShardId, DedupeKey) upsert is an enforced
/// database constraint, not only application logic: two calls reporting the same broken reference
/// converge on one row with an incremented count, and two different broken references never collide.
///
/// Issue #28 extends this with the Red section's "diagnostics access control" and "item/manifest
/// correlation" evidence: <see cref="CrossShardDiagnostic_IsRejected"/> proves the same
/// FK_CloudIconDiagnostic_Shard invariant <c>CloudCustodyRecordExclusivityTests.CrossShardOwnership_IsRejected</c>
/// already proves for custody records -- since ARCH-001 binds exactly one Cloud Shard per deployment,
/// this is the actual enforced boundary that makes "which shard's diagnostics" meaningful at all --
/// and <see cref="RecordAsync_ANewDiagnostic_CorrelatesTheProducingManifestVersion"/>/<see cref="RecordAsync_TheSameDiagnosticUnderALaterManifestVersion_UpdatesTheCorrelatedVersion"/>
/// prove the manifest-version correlation is recorded and kept current without disturbing dedup identity.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudIconDiagnosticGatewayTests
{
    private const string ShardId = "us1";
    private const string UnboundShardId = "us2";

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
            new CloudIconLayerReference(CloudIconLayerKind.Overlay, 0x06000030), CloudIconLayerResolutionOutcomeKind.Missing, manifestVersion: 1);

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
            new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000010), CloudIconLayerResolutionOutcomeKind.Corrupt, manifestVersion: 1);

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
            new CloudIconLayerReference(CloudIconLayerKind.Underlay, 0x06000020), CloudIconLayerResolutionOutcomeKind.Missing, manifestVersion: 1);
        var corrupt = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.Underlay, 0x06000020), CloudIconLayerResolutionOutcomeKind.Corrupt, manifestVersion: 1);

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

    [TestMethod]
    public async Task RecordAsync_ANewDiagnostic_CorrelatesTheProducingManifestVersion()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gateway = new CloudIconDiagnosticGateway(context);
        var diagnostic = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000050), CloudIconLayerResolutionOutcomeKind.Missing, manifestVersion: 3);

        await gateway.RecordAsync(ShardId, diagnostic, DateTime.UtcNow);

        var rows = await gateway.GetForShardAsync(ShardId);
        var row = rows.Single(d => d.DedupeKey == diagnostic.DedupeKey);
        Assert.AreEqual(3, row.LastSeenManifestVersion);
    }

    [TestMethod]
    public async Task RecordAsync_TheSameDiagnosticUnderALaterManifestVersion_UpdatesTheCorrelatedVersion()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var first = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.Overlay, 0x06000060), CloudIconLayerResolutionOutcomeKind.Corrupt, manifestVersion: 1);
        var reproducedUnderNewerManifest = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.Overlay, 0x06000060), CloudIconLayerResolutionOutcomeKind.Corrupt, manifestVersion: 2);

        await using (var context = new CloudDbContext(options))
        {
            await new CloudIconDiagnosticGateway(context).RecordAsync(ShardId, first, DateTime.UtcNow);
        }

        await using (var context = new CloudDbContext(options))
        {
            // Issue #28's Red scenario: the same broken DID reference reproduces after a manifest
            // upgrade (ASSET-002 activation). This must never fork into a second row (dedup identity
            // excludes manifest version) but must update which manifest most recently reproduced it.
            await new CloudIconDiagnosticGateway(context).RecordAsync(ShardId, reproducedUnderNewerManifest, DateTime.UtcNow.AddMinutes(1));
        }

        await using var verifyContext = new CloudDbContext(options);
        var gateway = new CloudIconDiagnosticGateway(verifyContext);
        var rows = await gateway.GetForShardAsync(ShardId);
        var matching = rows.Where(d => d.DedupeKey == first.DedupeKey).ToList();

        Assert.HasCount(1, matching, "The same underlying broken reference must remain one deduplicated row across manifest versions.");
        Assert.AreEqual(2, matching[0].OccurrenceCount);
        Assert.AreEqual(2, matching[0].LastSeenManifestVersion, "The correlated manifest version must reflect the most recent reproduction.");
    }

    [TestMethod]
    public async Task GetForShardAsync_OrdersByMostRecentlySeenFirst()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var older = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000070), CloudIconLayerResolutionOutcomeKind.Missing, manifestVersion: 1);
        var newer = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000080), CloudIconLayerResolutionOutcomeKind.Missing, manifestVersion: 1);

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudIconDiagnosticGateway(context);
            await gateway.RecordAsync(ShardId, older, DateTime.UtcNow);
            await gateway.RecordAsync(ShardId, newer, DateTime.UtcNow.AddMinutes(5));
        }

        await using var verifyContext = new CloudDbContext(options);
        var rows = await new CloudIconDiagnosticGateway(verifyContext).GetForShardAsync(ShardId);

        var newerIndex = rows.ToList().FindIndex(d => d.DedupeKey == newer.DedupeKey);
        var olderIndex = rows.ToList().FindIndex(d => d.DedupeKey == older.DedupeKey);
        Assert.IsLessThan(olderIndex, newerIndex, "The most recently seen diagnostic must be listed first.");
    }

    [TestMethod]
    public async Task GetForShardAsync_AnEmptyShardId_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gateway = new CloudIconDiagnosticGateway(context);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => gateway.GetForShardAsync(string.Empty));
    }

    [TestMethod]
    public async Task CrossShardDiagnostic_IsRejected()
    {
        // Issue #28's Red requirement: "diagnostics access control". ARCH-001 binds exactly one Cloud
        // Shard per deployment, so the FK_CloudIconDiagnostic_Shard constraint this proves is the
        // actual enforced boundary -- the same invariant
        // CloudCustodyRecordExclusivityTests.CrossShardOwnership_IsRejected already proves for custody
        // records -- that makes shard-scoped diagnostic access meaningful at all: it is structurally
        // impossible to record, and therefore impossible to later read back, a diagnostic against any
        // shard other than the one this deployment is bound to.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gateway = new CloudIconDiagnosticGateway(context);
        var diagnostic = new CloudIconCompositionDiagnostic(
            new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000090), CloudIconLayerResolutionOutcomeKind.Missing, manifestVersion: 1);

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => gateway.RecordAsync(UnboundShardId, diagnostic, DateTime.UtcNow));

        StringAssert.Contains(exception.Message, "FK_CloudIconDiagnostic_Shard");
    }
}
