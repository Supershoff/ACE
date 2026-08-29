using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #28's Red requirement: "Run an empty-environment import/golden flow, then simulate
/// interrupted upload, failed extraction, corrupt reference, cache poisoning attempt, manifest
/// upgrade, and future-version reprocessing." Every one of those scenarios already has its own
/// focused test elsewhere (<c>CloudAssetImportBoundaryTests</c> for interrupted upload/failed
/// extraction/manifest upgrade/reprocessing, <c>CloudIconCompositionCacheTests</c> for cache
/// poisoning, <c>CloudIconCompositorTests</c> for corrupt/missing references); this file's job,
/// following <c>CloudPhaseGateAcceptanceTests</c>' precedent for issue #17, is to prove they compose
/// correctly end to end from a truly empty environment: no active manifest, no diagnostics, one
/// Asset Import through to a rendered Icon Reconstruction and a manifest upgrade, entirely with
/// synthetic (never DAT-derived) fixtures so this always runs in ordinary CI (Refactor: "keep
/// synthetic CI coverage distinct from protected golden verification" -- the real curated corpus runs
/// separately via <c>CloudFidelityPhaseGateHarnessTests</c> on a protected operator workstation).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudFidelityPhaseGateAcceptanceTests
{
    private const string ShardId = "us1";
    private const uint ResolvableDid = 0x06000010;
    private const uint MissingDid = 0x06000099;

    private static CloudDatabaseFixture _fixture = null!;
    private static string _storageRoot = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();
        _storageRoot = Path.Combine(Path.GetTempPath(), "cloud-fidelity-phase-gate-acceptance-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_storageRoot);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();

        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await CloudBoundaryTestFixtureData.ResetAsync(_fixture.CloudConnectionString, ShardId);
    }

    [TestMethod]
    public async Task EmptyEnvironment_ThroughImportActivationCompositionAndManifestUpgrade_ProducesCorrelatedEvidence()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var storageOptions = new CloudAssetStorageOptions { RootDirectory = _storageRoot, MaxTotalBytes = 10_000, MaxChunkSizeBytes = 1_000 };
        var boundary = new CloudAssetImportBoundary(context, new LocalProtectedAssetBlobStore(storageOptions), storageOptions);
        var diagnostics = new CloudIconDiagnosticGateway(context);

        // Empty environment: nothing has ever activated, and no diagnostics have ever been recorded.
        Assert.IsNull(await boundary.GetActiveManifestAsync(ShardId, CloudAssetKind.Portal));
        Assert.HasCount(0, await diagnostics.GetForShardAsync(ShardId, maxCount: 1000));

        // The empty-environment import/golden flow: upload -> stage -> activate manifest version 1.
        var firstManifest = await UploadAndActivateAsync(boundary, entryCount: 2, adminAccountId: 5);
        Assert.AreEqual(1, firstManifest.Version);

        // Golden flow: composing a resolvable icon against the newly active manifest succeeds, and
        // composing a deliberately unresolvable one falls back and records a correlated diagnostic.
        var layerSource = new FakeLayerSource().WithResolved(ResolvableDid, 10, 20, 30, 255);
        var resolvableResult = await CloudIconCompositor.ComposeAsync(
            new CloudIconCompositionInputs { BaseIconDid = ResolvableDid }, firstManifest.Version, new NullClothingResolver(), layerSource);
        Assert.AreEqual(CloudIconCompositionOutcomeKind.Composed, resolvableResult.Outcome);

        var missingResult = await CloudIconCompositor.ComposeAsync(
            new CloudIconCompositionInputs { BaseIconDid = MissingDid }, firstManifest.Version, new NullClothingResolver(), layerSource);
        Assert.AreEqual(CloudIconCompositionOutcomeKind.Fallback, missingResult.Outcome);
        await diagnostics.RecordAsync(ShardId, missingResult.Diagnostics[0], DateTime.UtcNow);

        var recorded = (await diagnostics.GetForShardAsync(ShardId)).Single();
        Assert.AreEqual(firstManifest.Version, recorded.LastSeenManifestVersion, "The diagnostic must correlate to the manifest version active when it was produced.");

        // Full Cloud Appraisal is manifest-independent, but the same golden flow includes it: prove a
        // raw snapshot always projects to a deterministic, character-independent panel.
        var panel = CloudAppraisalProjector.Build(new CloudAppraisalRawItemSnapshot { ItemId = new CloudItemId(1), Name = "Test Buckler" });
        Assert.AreEqual("Test Buckler", panel.ItemName);

        // Future-version reprocessing + manifest upgrade: the retained source reprocesses into a
        // second manifest version without a re-upload, and activating it supersedes the first while
        // leaving it fully intact (rollback-to-previous-on-failure is proven directly by
        // CloudAssetImportBoundaryTests.FailedImportAfterAnActiveManifestExists_DoesNotDisturbTheActiveManifest;
        // this asserts the successful-upgrade half of the same lifecycle).
        var secondSession = (await boundary.ReprocessLatestRetainedAsync(ShardId, CloudAssetKind.Portal, adminAccountId: 5)).Value!;
        var secondManifest = (await boundary.CompleteStagingAsync(secondSession.Id, Guid.NewGuid(), SomeEntries(3))).Value!;
        await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, secondManifest.Version, adminAccountId: 5);

        var activeAfterUpgrade = await boundary.GetActiveManifestAsync(ShardId, CloudAssetKind.Portal);
        Assert.AreEqual(secondManifest.Id, activeAfterUpgrade!.Id);
        Assert.AreEqual(2, activeAfterUpgrade.Version);

        // The same broken reference reproducing under the upgraded manifest updates the correlated
        // version on the same deduplicated row rather than forking a second one (issue #28's "item/
        // manifest correlation", proven in isolation by CloudIconDiagnosticGatewayTests and here as
        // part of the composed flow).
        var missingAgainUnderV2 = await CloudIconCompositor.ComposeAsync(
            new CloudIconCompositionInputs { BaseIconDid = MissingDid }, secondManifest.Version, new NullClothingResolver(), layerSource);
        await diagnostics.RecordAsync(ShardId, missingAgainUnderV2.Diagnostics[0], DateTime.UtcNow.AddMinutes(1));

        var afterUpgrade = (await diagnostics.GetForShardAsync(ShardId)).Single();
        Assert.AreEqual(2, afterUpgrade.OccurrenceCount);
        Assert.AreEqual(2, afterUpgrade.LastSeenManifestVersion);

        // The phase-gate report itself: redacted, machine-readable, and identifies this run's coverage.
        // Both required categories (issue #28: "The protected phase gate must require non-empty Icon
        // and Appraisal corpora") must be represented, so the Appraisal panel this same flow already
        // built above is included as its own fixture result rather than leaving Appraisal uncovered.
        var report = CloudFidelityPhaseGateReport.Combine(
        [
            new CloudFidelityPhaseGateFixtureResult { Category = "Icon", FixtureName = "synthetic-resolvable", Matched = true },
            new CloudFidelityPhaseGateFixtureResult { Category = "Icon", FixtureName = "synthetic-missing-reference", Matched = true },
            new CloudFidelityPhaseGateFixtureResult { Category = "Appraisal", FixtureName = "synthetic-buckler", Matched = panel.ItemName == "Test Buckler" },
        ],
        nonBlockingGaps: ["This synthetic empty-environment run covers pipeline composition only; the curated real-DAT/real-capture corpus runs separately via the protected CloudFidelityPhaseGateHarnessTests."]);

        Assert.IsTrue(report.AllPassed);
        Assert.HasCount(0, report.MissingRequiredCategories);
        Assert.AreEqual(2, report.FixtureCountByCategory["Icon"]);
        Assert.AreEqual(1, report.FixtureCountByCategory["Appraisal"]);
    }

    private async Task<CloudAssetManifestSnapshot> UploadAndActivateAsync(CloudAssetImportBoundary boundary, int entryCount, uint adminAccountId)
    {
        var bytes = new byte[250];
        new Random(7).NextBytes(bytes);
        var checksumHex = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

        var session = (await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, adminAccountId, bytes.Length, chunkSizeBytes: 100, checksumHex)).Value!;

        const int chunkSize = 100;
        var chunkCount = (int)Math.Ceiling(bytes.Length / (double)chunkSize);
        for (var i = 0; i < chunkCount; i++)
        {
            var offset = i * chunkSize;
            var length = Math.Min(chunkSize, bytes.Length - offset);
            await boundary.ApplyChunkAsync(session.Id, i, new ReadOnlyMemory<byte>(bytes, offset, length));
        }

        await boundary.FinalizeUploadAsync(session.Id);
        var manifest = (await boundary.CompleteStagingAsync(session.Id, Guid.NewGuid(), SomeEntries(entryCount))).Value!;
        var activation = await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, manifest.Version, adminAccountId);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, activation.Kind, activation.Reason);

        return manifest;
    }

    private static IReadOnlyList<CloudAssetManifestEntryInput> SomeEntries(int count = 2) =>
        Enumerable.Range(0, count)
            .Select(i => new CloudAssetManifestEntryInput(
                new CloudAssetManifestEntryKey((uint)(0x06001000 + i), CloudAssetFileKind.Texture),
                $"relative/{i}", 128, new string('a', 64)))
            .ToList();

    private sealed class NullClothingResolver : ICloudIconClothingEffectResolver
    {
        public Task<CloudIconClothingResolution?> ResolveAsync(
            uint clothingBaseDid, uint setupTableId, int? paletteTemplate, float? shade, CancellationToken cancellationToken = default)
            => Task.FromResult<CloudIconClothingResolution?>(null);
    }

    private sealed class FakeLayerSource : ICloudIconLayerSource
    {
        private readonly Dictionary<uint, CloudIconRasterLayer> _resolved = new();

        public FakeLayerSource WithResolved(uint did, byte r, byte g, byte b, byte a)
        {
            _resolved[did] = new CloudIconRasterLayer(2, 2, [r, g, b, a, r, g, b, a, r, g, b, a, r, g, b, a]);
            return this;
        }

        public Task<CloudIconLayerResolution> ResolveAsync(
            CloudIconLayerReference reference, IReadOnlyList<CloudIconPaletteRangeOverride> paletteOverrides, CancellationToken cancellationToken = default) =>
            Task.FromResult(_resolved.TryGetValue(reference.Did, out var raster)
                ? CloudIconLayerResolution.Resolved(raster)
                : CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Missing));
    }
}
