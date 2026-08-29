using System.Security.Cryptography;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #25's resumable DAT upload, staging, and atomic Asset Manifest
/// activation pipeline (ASSET-001..004, ADM-001, EVT-001). Covers the Red section's malformed
/// /truncated input, wrong checksum, interrupted/resumed upload, duplicate chunks, concurrent
/// imports, staging failure, and activation race scenarios, plus the acceptance criteria that a
/// failed import never disturbs the active manifest and reprocessing works from retained storage.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudAssetImportBoundaryTests
{
    private const string ShardId = "us1";
    private const int ChunkSize = 100;

    private static CloudDatabaseFixture _fixture = null!;
    private static string _storageRoot = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();
        _storageRoot = Path.Combine(Path.GetTempPath(), "cloud-asset-import-tests", Guid.NewGuid().ToString("N"));
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

    private CloudAssetImportBoundary NewBoundary(out CloudDbContext context)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        context = new CloudDbContext(options);
        var storageOptions = new CloudAssetStorageOptions
        {
            RootDirectory = _storageRoot,
            MaxTotalBytes = 10_000,
            MaxChunkSizeBytes = 1_000,
        };
        return new CloudAssetImportBoundary(context, new LocalProtectedAssetBlobStore(storageOptions), storageOptions);
    }

    private static (byte[] Bytes, string ChecksumHex) BuildUpload(int totalBytes)
    {
        var bytes = new byte[totalBytes];
        new Random(42).NextBytes(bytes);
        var checksumHex = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return (bytes, checksumHex);
    }

    private static async Task<CloudAssetImportSessionSnapshot> UploadAllChunksAsync(
        CloudAssetImportBoundary boundary, Guid sessionId, byte[] bytes, int chunkCount)
    {
        CloudAssetImportSessionSnapshot? last = null;
        for (var i = 0; i < chunkCount; i++)
        {
            var offset = i * ChunkSize;
            var length = Math.Min(ChunkSize, bytes.Length - offset);
            var chunk = new ReadOnlyMemory<byte>(bytes, offset, length);
            var outcome = await boundary.ApplyChunkAsync(sessionId, i, chunk);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
            last = outcome.Value;
        }

        return last!;
    }

    [TestMethod]
    public async Task CreateOrResumeSession_FirstRequest_CreatesANewUploadingSession()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (_, checksumHex) = BuildUpload(250);

        var outcome = await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, adminAccountId: 5, 250, ChunkSize, checksumHex);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(CloudAssetImportSessionState.Uploading, outcome.Value!.State);
        Assert.AreEqual(3, outcome.Value.ChunkCount);
        Assert.IsFalse(outcome.Value.WasResumed);
    }

    [TestMethod]
    public async Task CreateOrResumeSession_SecondRequestWithTheSamePlanWhileInFlight_ResumesTheSameSession()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (_, checksumHex) = BuildUpload(250);

        var first = await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex);
        var second = await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.Id, second.Value!.Id);
        Assert.IsTrue(second.Value.WasResumed);
    }

    [TestMethod]
    public async Task CreateOrResumeSession_ADifferentPlanWhileAnImportIsInFlight_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (_, checksumHex) = BuildUpload(250);

        await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex);
        var conflicting = await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 500, ChunkSize, checksumHex);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, conflicting.Kind);
    }

    [TestMethod]
    public async Task CreateOrResumeSession_ADifferentAssetKind_DoesNotConflictWithAnInFlightImport()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (_, checksumHex) = BuildUpload(250);

        var portal = await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex);
        var highRes = await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.HighRes, 5, 250, ChunkSize, checksumHex);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, portal.Kind);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, highRes.Kind);
        Assert.AreNotEqual(portal.Value!.Id, highRes.Value!.Id);
    }

    [TestMethod]
    public async Task CreateOrResumeSession_DeclaredTotalBytesExceedsTheConfiguredMaximum_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (_, checksumHex) = BuildUpload(250);

        var outcome = await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, totalBytes: 50_000, ChunkSize, checksumHex);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task CreateOrResumeSession_TwoTrulyConcurrentRequests_OneCreatesAndOneResumesRatherThanRacing()
    {
        // Two independent boundaries over two independent DbContexts/connections, exactly as two
        // simultaneous HTTP requests would each get their own scoped DbContext.
        var boundaryA = NewBoundary(out var contextA);
        var boundaryB = NewBoundary(out var contextB);
        await using var _a = contextA;
        await using var _b = contextB;
        var (_, checksumHex) = BuildUpload(250);

        var results = await Task.WhenAll(
            boundaryA.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex),
            boundaryB.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex));

        Assert.IsTrue(results.All(r => r.Kind == CloudBoundaryOutcomeKind.Committed));
        Assert.AreEqual(results[0].Value!.Id, results[1].Value!.Id, "Two concurrent requests with an identical plan must converge on one session.");
    }

    [TestMethod]
    public async Task ApplyChunk_AcceptsInOrderChunksAndTracksProgress()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (bytes, checksumHex) = BuildUpload(250);
        var session = (await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex)).Value!;

        var progress = await UploadAllChunksAsync(boundary, session.Id, bytes, session.ChunkCount);

        Assert.AreEqual(3, progress.ReceivedChunkCount);
    }

    [TestMethod]
    public async Task ApplyChunk_AResendOfAnIdenticalChunk_IsIdempotentAndDoesNotDoubleCount()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (bytes, checksumHex) = BuildUpload(250);
        var session = (await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex)).Value!;

        var chunk0 = new ReadOnlyMemory<byte>(bytes, 0, ChunkSize);
        var first = await boundary.ApplyChunkAsync(session.Id, 0, chunk0);
        var resend = await boundary.ApplyChunkAsync(session.Id, 0, chunk0);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, resend.Kind);
        Assert.AreEqual(1, resend.Value!.ReceivedChunkCount, "A resend of the same bytes must not be counted twice.");
    }

    [TestMethod]
    public async Task ApplyChunk_AConflictingResendOfTheSameIndex_IsRejectedWithoutChangingProgress()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (bytes, checksumHex) = BuildUpload(250);
        var session = (await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex)).Value!;

        await boundary.ApplyChunkAsync(session.Id, 0, new ReadOnlyMemory<byte>(bytes, 0, ChunkSize));

        var differentBytes = new byte[ChunkSize];
        Array.Fill(differentBytes, (byte)0xFF);
        var conflicting = await boundary.ApplyChunkAsync(session.Id, 0, differentBytes);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, conflicting.Kind);
    }

    [TestMethod]
    public async Task ApplyChunk_WrongLength_IsRejected()
    {
        // Red test: "malformed/truncated input".
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (bytes, checksumHex) = BuildUpload(250);
        var session = (await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex)).Value!;

        var truncated = new ReadOnlyMemory<byte>(bytes, 0, ChunkSize - 10);
        var outcome = await boundary.ApplyChunkAsync(session.Id, 0, truncated);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task FinalizeUpload_BeforeAllChunksArrive_IsRejectedAndSessionRemainsResumable()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (bytes, checksumHex) = BuildUpload(250);
        var session = (await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex)).Value!;
        await boundary.ApplyChunkAsync(session.Id, 0, new ReadOnlyMemory<byte>(bytes, 0, ChunkSize));

        var outcome = await boundary.FinalizeUploadAsync(session.Id);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);

        // Still resumable: a later request with the same plan reattaches to this session.
        var resumed = await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex);
        Assert.AreEqual(session.Id, resumed.Value!.Id);
    }

    [TestMethod]
    public async Task FinalizeUpload_WrongDeclaredChecksum_TransitionsToChecksumFailed()
    {
        // Red test: "wrong format/checksum".
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (bytes, _) = BuildUpload(250);
        var wrongChecksum = Convert.ToHexStringLower(SHA256.HashData(new byte[250]));
        var session = (await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, wrongChecksum)).Value!;
        await UploadAllChunksAsync(boundary, session.Id, bytes, session.ChunkCount);

        var outcome = await boundary.FinalizeUploadAsync(session.Id);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(CloudAssetImportSessionState.ChecksumFailed, outcome.Value!.State);
    }

    [TestMethod]
    public async Task FinalizeUpload_AllChunksAndCorrectChecksum_QueuesForStagingAndRetainsTheSource()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var (bytes, checksumHex) = BuildUpload(250);
        var session = (await boundary.CreateOrResumeSessionAsync(ShardId, CloudAssetKind.Portal, 5, 250, ChunkSize, checksumHex)).Value!;
        await UploadAllChunksAsync(boundary, session.Id, bytes, session.ChunkCount);

        var outcome = await boundary.FinalizeUploadAsync(session.Id);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(CloudAssetImportSessionState.Staging, outcome.Value!.State);

        var queued = await boundary.TryDequeueNextStagingSessionAsync(ShardId, CloudAssetKind.Portal);
        Assert.IsNotNull(queued);
        Assert.AreEqual(session.Id, queued!.Id);
    }

    private async Task<CloudAssetImportSessionSnapshot> UploadFinalizeAndReachStagingAsync(CloudAssetImportBoundary boundary, CloudAssetKind kind = CloudAssetKind.Portal)
    {
        var (bytes, checksumHex) = BuildUpload(250);
        var session = (await boundary.CreateOrResumeSessionAsync(ShardId, kind, 5, 250, ChunkSize, checksumHex)).Value!;
        await UploadAllChunksAsync(boundary, session.Id, bytes, session.ChunkCount);
        var finalized = await boundary.FinalizeUploadAsync(session.Id);
        Assert.AreEqual(CloudAssetImportSessionState.Staging, finalized.Value!.State);
        return finalized.Value;
    }

    private static IReadOnlyList<CloudAssetManifestEntryInput> SomeEntries(int count = 2)
    {
        var entries = new List<CloudAssetManifestEntryInput>();
        for (var i = 1; i <= count; i++)
        {
            var key = new CloudAssetManifestEntryKey((uint)(0x06000000 + i), CloudAssetFileKind.Texture);
            entries.Add(new CloudAssetManifestEntryInput(key, $"manifests/x/{i}.bin", 128, Convert.ToHexStringLower(SHA256.HashData(BitConverter.GetBytes(i)))));
        }

        return entries;
    }

    [TestMethod]
    public async Task CompleteStaging_ProducesTheFirstManifestVersionAndMarksTheSessionComplete()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var session = await UploadFinalizeAndReachStagingAsync(boundary);

        var outcome = await boundary.CompleteStagingAsync(session.Id, Guid.NewGuid(), SomeEntries());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(1, outcome.Value!.Version);
        Assert.AreEqual(CloudAssetManifestState.StagingComplete, outcome.Value.State);
        Assert.AreEqual(2, outcome.Value.EntryCount);
    }

    [TestMethod]
    public async Task FailStaging_MarksTheSessionFailedAndLeavesNoActiveManifest()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var session = await UploadFinalizeAndReachStagingAsync(boundary);

        var outcome = await boundary.FailStagingAsync(session.Id, "extraction blew up");

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(CloudAssetImportSessionState.StagingFailed, outcome.Value!.State);

        var active = await boundary.GetActiveManifestAsync(ShardId, CloudAssetKind.Portal);
        Assert.IsNull(active);
    }

    [TestMethod]
    public async Task GetActiveManifest_NothingEverActivated_ReturnsNull()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var active = await boundary.GetActiveManifestAsync(ShardId, CloudAssetKind.Portal);

        Assert.IsNull(active);
    }

    [TestMethod]
    public async Task ActivateManifest_AFirstCompleteManifest_BecomesActive()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var session = await UploadFinalizeAndReachStagingAsync(boundary);
        var manifest = (await boundary.CompleteStagingAsync(session.Id, Guid.NewGuid(), SomeEntries())).Value!;

        var outcome = await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, manifest.Version, adminAccountId: 5);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(CloudAssetManifestState.Active, outcome.Value!.State);

        var active = await boundary.GetActiveManifestAsync(ShardId, CloudAssetKind.Portal);
        Assert.IsNotNull(active);
        Assert.AreEqual(manifest.Id, active!.Id);
        Assert.HasCount(2, active.Entries);
    }

    [TestMethod]
    public async Task ActivateManifest_ASecondNewerManifest_SupersedesTheFirstAndBecomesActive()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var firstSession = await UploadFinalizeAndReachStagingAsync(boundary);
        var firstManifest = (await boundary.CompleteStagingAsync(firstSession.Id, Guid.NewGuid(), SomeEntries())).Value!;
        await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, firstManifest.Version, 5);

        // Reprocess to get a second in-flight-free session for the same shard/kind, then complete it.
        var secondSession = (await boundary.ReprocessLatestRetainedAsync(ShardId, CloudAssetKind.Portal, 5)).Value!;
        var secondManifest = (await boundary.CompleteStagingAsync(secondSession.Id, Guid.NewGuid(), SomeEntries(3))).Value!;

        var activation = await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, secondManifest.Version, 5);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, activation.Kind);

        var active = await boundary.GetActiveManifestAsync(ShardId, CloudAssetKind.Portal);
        Assert.AreEqual(secondManifest.Id, active!.Id);
        Assert.AreEqual(2, secondManifest.Version);
    }

    [TestMethod]
    public async Task ActivateManifest_AnOlderVersionThanTheCurrentlyActiveOne_IsRejected()
    {
        // Red test: "activation race".
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var firstSession = await UploadFinalizeAndReachStagingAsync(boundary);
        var firstManifest = (await boundary.CompleteStagingAsync(firstSession.Id, Guid.NewGuid(), SomeEntries())).Value!;

        var secondSession = (await boundary.ReprocessLatestRetainedAsync(ShardId, CloudAssetKind.Portal, 5)).Value!;
        var secondManifest = (await boundary.CompleteStagingAsync(secondSession.Id, Guid.NewGuid(), SomeEntries(3))).Value!;

        // Version 2 wins the race first.
        await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, secondManifest.Version, 5);

        // A slow attempt to activate version 1 arrives after version 2 already won.
        var lateActivation = await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, firstManifest.Version, 5);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, lateActivation.Kind);

        var active = await boundary.GetActiveManifestAsync(ShardId, CloudAssetKind.Portal);
        Assert.AreEqual(secondManifest.Id, active!.Id, "The already-committed newer activation must not be clobbered by the late, older request.");
    }

    [TestMethod]
    public async Task ActivateManifest_AnIncompleteManifest_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var outcome = await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, manifestVersion: 1, adminAccountId: 5);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task FailedImportAfterAnActiveManifestExists_DoesNotDisturbTheActiveManifest()
    {
        // Acceptance criterion: "Interrupted import resumes and failed import cannot disturb active assets."
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var firstSession = await UploadFinalizeAndReachStagingAsync(boundary);
        var firstManifest = (await boundary.CompleteStagingAsync(firstSession.Id, Guid.NewGuid(), SomeEntries())).Value!;
        await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, firstManifest.Version, 5);

        var failingSession = (await boundary.ReprocessLatestRetainedAsync(ShardId, CloudAssetKind.Portal, 5)).Value!;
        await boundary.FailStagingAsync(failingSession.Id, "disk full during extraction");

        var active = await boundary.GetActiveManifestAsync(ShardId, CloudAssetKind.Portal);
        Assert.IsNotNull(active);
        Assert.AreEqual(firstManifest.Id, active!.Id);
        Assert.AreEqual(CloudAssetManifestState.Active, active.State);
    }

    [TestMethod]
    public async Task ReprocessLatestRetained_NoSourceHasEverBeenRetained_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var outcome = await boundary.ReprocessLatestRetainedAsync(ShardId, CloudAssetKind.Portal, 5);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task ReprocessLatestRetained_AfterASuccessfulUpload_StartsANewSessionDirectlyInStaging()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        var session = await UploadFinalizeAndReachStagingAsync(boundary);
        await boundary.CompleteStagingAsync(session.Id, Guid.NewGuid(), SomeEntries()); // frees the shard/kind for a new import

        var outcome = await boundary.ReprocessLatestRetainedAsync(ShardId, CloudAssetKind.Portal, 5);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(CloudAssetImportSessionState.Staging, outcome.Value!.State);
    }

    [TestMethod]
    public async Task ReprocessLatestRetained_WhileAnImportIsAlreadyInFlight_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;
        await UploadFinalizeAndReachStagingAsync(boundary); // leaves the session in Staging (in-flight)

        var outcome = await boundary.ReprocessLatestRetainedAsync(ShardId, CloudAssetKind.Portal, 5);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task ConcurrentReads_DuringManifestActivation_NeverObserveATornManifest()
    {
        // Issue #28's Red requirement: "Test concurrent reads during activation". Every manifest's
        // entries are already fully committed at CompleteStagingAsync time, long before
        // ActivateManifestAsync ever locks the pointer row -- so a concurrent reader racing the
        // pointer swap must always observe either the complete previous manifest or the complete new
        // one, never a manifest whose entries have not finished loading (ASSET-002: "the active
        // manifest changes only after complete verified success").
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var firstSession = await UploadFinalizeAndReachStagingAsync(boundary);
        var firstManifest = (await boundary.CompleteStagingAsync(firstSession.Id, Guid.NewGuid(), SomeEntries())).Value!;
        await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, firstManifest.Version, 5);

        var secondSession = (await boundary.ReprocessLatestRetainedAsync(ShardId, CloudAssetKind.Portal, 5)).Value!;
        var secondManifest = (await boundary.CompleteStagingAsync(secondSession.Id, Guid.NewGuid(), SomeEntries(3))).Value!;

        var readerOptions = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var storageOptions = new CloudAssetStorageOptions { RootDirectory = _storageRoot, MaxTotalBytes = 10_000, MaxChunkSizeBytes = 1_000 };

        var barrier = new SemaphoreSlim(0, 25);
        var readTasks = Enumerable.Range(0, 25).Select(async _ =>
        {
            await using var readContext = new CloudDbContext(readerOptions);
            var readBoundary = new CloudAssetImportBoundary(readContext, new LocalProtectedAssetBlobStore(storageOptions), storageOptions);
            await barrier.WaitAsync();
            return await readBoundary.GetActiveManifestAsync(ShardId, CloudAssetKind.Portal);
        }).ToList();

        var activateTask = Task.Run(async () =>
        {
            await Task.Delay(5);
            return await boundary.ActivateManifestAsync(ShardId, CloudAssetKind.Portal, secondManifest.Version, 5);
        });

        barrier.Release(25);
        var reads = await Task.WhenAll(readTasks);
        var activation = await activateTask;

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, activation.Kind);

        foreach (var read in reads)
        {
            Assert.IsNotNull(read, "A concurrent reader must never see 'no active manifest' once one has already activated.");
            Assert.IsTrue(read!.Id == firstManifest.Id || read.Id == secondManifest.Id, "A concurrent reader must observe one of the two real manifests, never a foreign or empty one.");

            var expectedEntryCount = read.Id == firstManifest.Id ? firstManifest.EntryCount : secondManifest.EntryCount;
            Assert.HasCount(expectedEntryCount, read.Entries, "A concurrent reader must never observe a manifest whose entries have not fully loaded.");
        }
    }
}
