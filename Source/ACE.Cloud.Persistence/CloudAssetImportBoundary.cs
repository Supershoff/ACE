using System.Security.Cryptography;
using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's transaction boundary for Asset Import (ASSET-001..004,
/// ADM-001, EVT-001): resumable chunked upload, checksum-verified finalization into a queued
/// staging session, background extraction handoff, and atomic manifest activation. Every mutating
/// method locks the row(s) it decides against for the whole transaction, converts them into the pure
/// domain policies in <c>ACE.Cloud.Domain</c>, and only on approval persists the result and commits
/// -- the same locked-revalidate-then-commit shape <see cref="CloudCustodyBoundary"/> and
/// <see cref="CloudCustodianConfigurationBoundary"/> already use.
///
/// A policy rejection that changes nothing (session/manifest not found, wrong state, not yet
/// complete) returns <see cref="CloudBoundaryOutcomeKind.Conflict"/> without committing any row.
/// A policy rejection that itself is audit-worthy (a checksum failure, a failed staging attempt, a
/// rejected activation) commits its ledger event and terminal state before returning either
/// <see cref="CloudBoundaryOutcomeKind.Committed"/> (the session/manifest itself transitioned, e.g.
/// to <see cref="CloudAssetImportSessionState.ChecksumFailed"/>) or Conflict (an activation attempt
/// that never changed the active pointer). This mirrors <c>CloudAccountLinkGateway</c>'s precedent of
/// auditing a rejection in the same transaction as the decision that produced it.
/// </summary>
public sealed class CloudAssetImportBoundary
{
    private readonly CloudDbContext _context;
    private readonly IProtectedAssetBlobStore _blobStore;
    private readonly CloudAssetStorageOptions _storageOptions;

    public CloudAssetImportBoundary(CloudDbContext context, IProtectedAssetBlobStore blobStore, CloudAssetStorageOptions storageOptions)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _blobStore = blobStore ?? throw new ArgumentNullException(nameof(blobStore));
        _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
    }

    /// <summary>
    /// Starts a new Asset Import, or -- if one is already in flight for this shard/kind with the
    /// exact same declared plan -- returns it for resume (ASSET-002: "interrupted/resumed upload";
    /// Red test: "concurrent imports"). A concurrent request with a different declared plan is
    /// rejected instead of silently racing the in-flight one.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> CreateOrResumeSessionAsync(
        string shardId, CloudAssetKind kind, uint adminAccountId, long totalBytes, int chunkSizeBytes, string expectedChecksumHex,
        CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(
            () => CreateOrResumeSessionOnceAsync(shardId, kind, adminAccountId, totalBytes, chunkSizeBytes, expectedChecksumHex, cancellationToken),
            cancellationToken: cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> CreateOrResumeSessionOnceAsync(
        string shardId, CloudAssetKind kind, uint adminAccountId, long totalBytes, int chunkSizeBytes, string expectedChecksumHex,
        CancellationToken cancellationToken)
    {
        RequireShardId(shardId);

        if (adminAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(adminAccountId));
        }

        if (!CloudAssetChecksum.TryParse(expectedChecksumHex, out var expectedChecksum))
        {
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict("The declared checksum is not a valid 64-character hex SHA-256 digest.");
        }

        var sizeDecision = CloudAssetImportSessionRequestPolicy.Evaluate(totalBytes, chunkSizeBytes, _storageOptions.MaxTotalBytes, _storageOptions.MaxChunkSizeBytes);
        if (!sizeDecision.IsValid)
        {
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict(sizeDecision.Reason!);
        }

        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var marker = await LockCurrentSessionMarkerAsync(shardId, kind, cancellationToken);
        var existingSession = marker is null
            ? null
            : await _context.Set<CloudAssetImportSession>().SingleAsync(s => s.Id == marker.SessionId, cancellationToken);

        if (existingSession is not null && CloudAssetImportConcurrencyPolicy.IsInFlight(existingSession.State))
        {
            var plan = new CloudAssetImportChunkPlan(totalBytes, chunkSizeBytes, expectedChecksum);
            var samePlan = existingSession.TotalBytes == totalBytes
                && existingSession.ChunkSizeBytes == chunkSizeBytes
                && existingSession.ChunkCount == plan.ChunkCount
                && string.Equals(existingSession.ExpectedChecksumHex, expectedChecksum.Value, StringComparison.Ordinal);

            await transaction.RollbackAsync(cancellationToken);

            if (!samePlan)
            {
                return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict(
                    $"An Asset Import for {shardId}/{kind} is already in flight (session {existingSession.Id}) with a different declared plan.");
            }

            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Committed(CloudAssetImportSessionSnapshot.From(existingSession, wasResumed: true));
        }

        var session = new CloudAssetImportSession(
            Guid.NewGuid(), shardId, kind, totalBytes, chunkSizeBytes, new CloudAssetImportChunkPlan(totalBytes, chunkSizeBytes, expectedChecksum).ChunkCount,
            expectedChecksum.Value, adminAccountId);
        _context.Add(session);

        if (marker is null)
        {
            _context.Add(new CloudAssetImportCurrentSessionMarker(shardId, kind, session.Id));
        }
        else
        {
            marker.PointTo(session.Id);
            _context.Update(marker);
        }

        _context.Add(new CloudAssetImportLedgerEvent(
            Guid.NewGuid(), shardId, kind, CloudAssetImportLedgerEventType.Started, session.Id, manifestId: null, manifestVersion: null, adminAccountId, reason: null));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Committed(CloudAssetImportSessionSnapshot.From(session));
    }

    /// <summary>
    /// Applies one uploaded chunk (ASSET-002's Red tests: "malformed/truncated input", "duplicate
    /// chunks", "interrupted/resumed upload"). Bytes are written to protected storage only when the
    /// chunk is newly accepted; a rejected or duplicate chunk never touches storage.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> ApplyChunkAsync(
        Guid sessionId, int chunkIndex, ReadOnlyMemory<byte> chunkBytes, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(() => ApplyChunkOnceAsync(sessionId, chunkIndex, chunkBytes, cancellationToken), cancellationToken: cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> ApplyChunkOnceAsync(
        Guid sessionId, int chunkIndex, ReadOnlyMemory<byte> chunkBytes, CancellationToken cancellationToken)
    {
        var computedChecksumHex = Convert.ToHexStringLower(SHA256.HashData(chunkBytes.Span));
        CloudAssetChecksum.TryParse(computedChecksumHex, out var computedChecksum);

        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var session = await LockSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict($"No Asset Import session {sessionId} exists.");
        }

        var plan = session.ToChunkPlan();

        var existingChunk = await _context.Set<CloudAssetImportChunkRecord>().AsNoTracking()
            .SingleOrDefaultAsync(c => c.SessionId == sessionId && c.ChunkIndex == chunkIndex, cancellationToken);
        CloudAssetChecksum? existingChecksum = null;
        if (existingChunk is not null && CloudAssetChecksum.TryParse(existingChunk.Sha256Hex, out var parsedExisting))
        {
            existingChecksum = parsedExisting;
        }

        var decision = CloudAssetImportUploadPolicy.EvaluateChunk(session.State, plan, chunkIndex, chunkBytes.Length, computedChecksum, existingChecksum);

        if (decision.Kind == CloudAssetImportChunkOutcomeKind.Rejected)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict(decision.RejectionReason!);
        }

        if (decision.Kind == CloudAssetImportChunkOutcomeKind.DuplicateIgnored)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Committed(CloudAssetImportSessionSnapshot.From(session));
        }

        // Accepted: persist the bytes before recording the chunk row, so a crash between the two
        // just leaves an unreferenced-but-harmless file that a resumed re-send safely overwrites.
        await _blobStore.WriteAsync(CloudAssetStagingPathPolicy.BuildChunkPartRelativePath(sessionId, chunkIndex), chunkBytes, cancellationToken);

        _context.Add(new CloudAssetImportChunkRecord(sessionId, chunkIndex, computedChecksum.Value, chunkBytes.Length));
        session.RecordAcceptedChunk();
        _context.Update(session);

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Committed(CloudAssetImportSessionSnapshot.From(session));
    }

    /// <summary>
    /// Assembles every received chunk in order, verifies the result against the session's declared
    /// checksum (ASSET-002's Red test: "wrong format/checksum"), and -- only on success -- queues
    /// the session for background staging and retains the assembled bytes as the shard/kind's latest
    /// source (ASSET-003). The heavy chunk-concatenation/hashing I/O runs before any row lock is
    /// taken; only the (fast) state transition itself happens under lock.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> FinalizeUploadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var precheck = await _context.Set<CloudAssetImportSession>().AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (precheck is null)
        {
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict($"No Asset Import session {sessionId} exists.");
        }

        var plan = precheck.ToChunkPlan();
        var earlyDecision = CloudAssetImportUploadPolicy.EvaluateFinalization(precheck.State, plan, precheck.ReceivedChunkCount, plan.ExpectedChecksum);
        if (earlyDecision.Kind is CloudAssetUploadFinalizationOutcomeKind.InvalidState or CloudAssetUploadFinalizationOutcomeKind.Incomplete)
        {
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict(earlyDecision.Reason!);
        }

        var computedChecksumHex = await AssembleAndHashAsync(sessionId, plan.ChunkCount, cancellationToken);

        return await CloudBoundaryRetry.ExecuteAsync(
            () => FinalizeUploadOnceAsync(sessionId, computedChecksumHex, cancellationToken), cancellationToken: cancellationToken);
    }

    private async Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> FinalizeUploadOnceAsync(
        Guid sessionId, string computedChecksumHex, CancellationToken cancellationToken)
    {
        CloudAssetChecksum.TryParse(computedChecksumHex, out var computedChecksum);

        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var session = await LockSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict($"No Asset Import session {sessionId} exists.");
        }

        var plan = session.ToChunkPlan();
        var decision = CloudAssetImportUploadPolicy.EvaluateFinalization(session.State, plan, session.ReceivedChunkCount, computedChecksum);

        if (decision.Kind is CloudAssetUploadFinalizationOutcomeKind.InvalidState or CloudAssetUploadFinalizationOutcomeKind.Incomplete)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict(decision.Reason!);
        }

        if (decision.Kind == CloudAssetUploadFinalizationOutcomeKind.ChecksumMismatch)
        {
            session.MarkChecksumFailed(decision.Reason!);
            _context.Update(session);
            _context.Add(new CloudAssetImportLedgerEvent(
                Guid.NewGuid(), session.ShardId, session.Kind, CloudAssetImportLedgerEventType.ChecksumFailed,
                session.Id, manifestId: null, manifestVersion: null, session.InitiatedByAccountId, decision.Reason));

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Committed(CloudAssetImportSessionSnapshot.From(session));
        }

        // Completed: queue for background staging and retain this checksum-verified upload as the
        // shard/kind's latest source (ASSET-003), replacing whatever was retained before.
        session.MarkQueuedForStaging();
        _context.Update(session);

        var retainedPath = CloudAssetStagingPathPolicy.BuildRetainedSourceRelativePath(session.ShardId, session.Kind);
        await _blobStore.CopyAsync(CloudAssetStagingPathPolicy.BuildAssembledUploadRelativePath(sessionId), retainedPath, cancellationToken);

        var existingRetained = await LockRetainedSourceAsync(session.ShardId, session.Kind, cancellationToken);
        if (existingRetained is null)
        {
            _context.Add(new CloudRetainedSourceAsset(session.ShardId, session.Kind, retainedPath, session.TotalBytes, computedChecksum.Value, session.Id));
        }
        else
        {
            existingRetained.ReplaceWith(retainedPath, session.TotalBytes, computedChecksum.Value, session.Id);
            _context.Update(existingRetained);
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Committed(CloudAssetImportSessionSnapshot.From(session));
    }

    /// <summary>
    /// Returns the oldest session currently queued for staging, or null if none is (the background
    /// worker's poll loop). Deliberately a plain unlocked read: exactly one deployment's worker
    /// process is expected to run per shard (the Companion Stack's own "background workers"), and
    /// re-running idempotent extraction against the same session after a worker crash and restart is
    /// safe and simpler than a lease/claim protocol (ASSET-002's Red test: "worker crash").
    /// </summary>
    public async Task<CloudAssetImportSessionSnapshot?> TryDequeueNextStagingSessionAsync(string shardId, CloudAssetKind kind, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);

        var session = await _context.Set<CloudAssetImportSession>().AsNoTracking()
            .Where(s => s.ShardId == shardId && s.Kind == kind && s.State == CloudAssetImportSessionState.Staging)
            .OrderBy(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return session is null ? null : CloudAssetImportSessionSnapshot.From(session);
    }

    /// <summary>
    /// Records a successful extraction as a new, immediately complete manifest version (ASSET-002,
    /// ASSET-004). <paramref name="manifestId"/> is caller-supplied rather than generated here
    /// because the caller (the staging worker) must already know it before extraction starts, to
    /// build each entry's staging path via <see cref="CloudAssetStagingPathPolicy.BuildManifestEntryRelativePath"/>.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudAssetManifestSnapshot>> CompleteStagingAsync(
        Guid sessionId, Guid manifestId, IReadOnlyList<CloudAssetManifestEntryInput> entries, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(() => CompleteStagingOnceAsync(sessionId, manifestId, entries, cancellationToken), cancellationToken: cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudAssetManifestSnapshot>> CompleteStagingOnceAsync(
        Guid sessionId, Guid manifestId, IReadOnlyList<CloudAssetManifestEntryInput> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (manifestId == Guid.Empty)
        {
            throw new ArgumentException("Completing staging requires a real manifest ID.", nameof(manifestId));
        }

        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var session = await LockSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetManifestSnapshot>.Conflict($"No Asset Import session {sessionId} exists.");
        }

        if (session.State != CloudAssetImportSessionState.Staging)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetManifestSnapshot>.Conflict(
                $"Session {sessionId} is in state {session.State}, not {CloudAssetImportSessionState.Staging}; it cannot complete staging.");
        }

        if (entries.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetManifestSnapshot>.Conflict("A completed staging pass produced no manifest entries.");
        }

        var maxExistingVersion = await _context.Set<CloudAssetManifest>()
            .Where(m => m.ShardId == session.ShardId && m.Kind == session.Kind)
            .Select(m => (int?)m.Version)
            .MaxAsync(cancellationToken);
        var nextVersion = (maxExistingVersion ?? 0) + 1;

        var manifest = new CloudAssetManifest(manifestId, session.ShardId, session.Kind, nextVersion, sessionId, entries.Count);
        _context.Add(manifest);

        foreach (var entry in entries)
        {
            _context.Add(new CloudAssetManifestEntryRecord(manifest.Id, entry.Key.Did, entry.Key.Kind, entry.RelativePath, entry.ByteLength, entry.Sha256Hex));
        }

        session.MarkStagingComplete(manifest.Id);
        _context.Update(session);

        _context.Add(new CloudAssetImportLedgerEvent(
            Guid.NewGuid(), session.ShardId, session.Kind, CloudAssetImportLedgerEventType.StagingCompleted,
            session.Id, manifest.Id, manifest.Version, session.InitiatedByAccountId, reason: null));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudAssetManifestSnapshot>.Committed(CloudAssetManifestSnapshot.From(manifest));
    }

    /// <summary>
    /// Records a failed extraction attempt. The active manifest, if any, is never touched (the
    /// acceptance criterion "failed import cannot disturb active assets" is structural here: nothing
    /// in this method ever reads or writes <see cref="CloudActiveAssetManifest"/>).
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> FailStagingAsync(Guid sessionId, string errorMessage, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(() => FailStagingOnceAsync(sessionId, errorMessage, cancellationToken), cancellationToken: cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> FailStagingOnceAsync(Guid sessionId, string errorMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("A failed staging attempt requires an error message.", nameof(errorMessage));
        }

        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var session = await LockSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict($"No Asset Import session {sessionId} exists.");
        }

        if (session.State != CloudAssetImportSessionState.Staging)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict(
                $"Session {sessionId} is in state {session.State}, not {CloudAssetImportSessionState.Staging}; there is no staging attempt to fail.");
        }

        session.MarkStagingFailed(errorMessage);
        _context.Update(session);

        _context.Add(new CloudAssetImportLedgerEvent(
            Guid.NewGuid(), session.ShardId, session.Kind, CloudAssetImportLedgerEventType.StagingFailed,
            session.Id, manifestId: null, manifestVersion: null, session.InitiatedByAccountId, errorMessage));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Committed(CloudAssetImportSessionSnapshot.From(session));
    }

    /// <summary>
    /// Activates a completed manifest version with one locked pointer swap (ASSET-002's Red test:
    /// "activation race"): the previously active manifest (if any) becomes
    /// <see cref="CloudAssetManifestState.Superseded"/> and the requested version becomes
    /// <see cref="CloudAssetManifestState.Active"/> in the same transaction, or neither changes.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudAssetManifestSnapshot>> ActivateManifestAsync(
        string shardId, CloudAssetKind kind, int manifestVersion, uint adminAccountId, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(() => ActivateManifestOnceAsync(shardId, kind, manifestVersion, adminAccountId, cancellationToken), cancellationToken: cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudAssetManifestSnapshot>> ActivateManifestOnceAsync(
        string shardId, CloudAssetKind kind, int manifestVersion, uint adminAccountId, CancellationToken cancellationToken)
    {
        RequireShardId(shardId);

        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var activePointer = await LockActiveManifestPointerAsync(shardId, kind, cancellationToken);
        var target = await LockManifestAsync(shardId, kind, manifestVersion, cancellationToken);

        if (target is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetManifestSnapshot>.Conflict($"No manifest version {manifestVersion} exists for {shardId}/{kind}.");
        }

        var decision = CloudAssetManifestActivationPolicy.Evaluate(target.State, target.Version, target.EntryCount, activePointer?.ManifestVersion);

        if (!decision.IsApproved)
        {
            _context.Add(new CloudAssetImportLedgerEvent(
                Guid.NewGuid(), shardId, kind, CloudAssetImportLedgerEventType.ManifestActivationRejected,
                sessionId: null, target.Id, target.Version, adminAccountId, decision.RejectionReason));

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetManifestSnapshot>.Conflict(decision.RejectionReason!);
        }

        var nowUtc = await GetDatabaseUtcNowAsync(cancellationToken);

        if (activePointer is not null)
        {
            var previouslyActive = await _context.Set<CloudAssetManifest>().SingleAsync(m => m.Id == activePointer.ManifestId, cancellationToken);
            previouslyActive.MarkSuperseded(nowUtc);
            _context.Update(previouslyActive);
        }

        target.MarkActive(nowUtc);
        _context.Update(target);

        if (activePointer is null)
        {
            _context.Add(new CloudActiveAssetManifest(shardId, kind, target.Id, target.Version));
        }
        else
        {
            activePointer.PointTo(target.Id, target.Version);
            _context.Update(activePointer);
        }

        _context.Add(new CloudAssetImportLedgerEvent(
            Guid.NewGuid(), shardId, kind, CloudAssetImportLedgerEventType.ManifestActivated,
            sessionId: null, target.Id, target.Version, adminAccountId, reason: null));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudAssetManifestSnapshot>.Committed(CloudAssetManifestSnapshot.From(target));
    }

    /// <summary>
    /// Reads the currently active manifest and its entries for <paramref name="shardId"/>/<paramref name="kind"/>,
    /// or null if none has ever activated. The currently active assets remain available continuously
    /// through any later failed import (ASSET-002's "the previously active manifest serves
    /// continuously") because this read never touches <see cref="CloudAssetImportSession"/> at all.
    /// </summary>
    public async Task<CloudAssetManifestSnapshot?> GetActiveManifestAsync(string shardId, CloudAssetKind kind, CancellationToken cancellationToken = default)
    {
        RequireShardId(shardId);

        var pointer = await _context.Set<CloudActiveAssetManifest>().AsNoTracking()
            .SingleOrDefaultAsync(p => p.ShardId == shardId && p.Kind == kind, cancellationToken);
        if (pointer is null)
        {
            return null;
        }

        var manifest = await _context.Set<CloudAssetManifest>().AsNoTracking().SingleAsync(m => m.Id == pointer.ManifestId, cancellationToken);
        var entries = await _context.Set<CloudAssetManifestEntryRecord>().AsNoTracking()
            .Where(e => e.ManifestId == manifest.Id)
            .Select(e => new CloudAssetManifestEntrySnapshot(e.Did, e.FileKind, e.RelativePath, e.ByteLength, e.Sha256Hex))
            .ToListAsync(cancellationToken);

        return CloudAssetManifestSnapshot.From(manifest, entries);
    }

    /// <summary>
    /// Starts a new import sourced from the already-retained latest source DAT, skipping upload
    /// entirely (ASSET-003: "Admin may upload changed DATs" implies reprocessing the one already
    /// held requires no re-upload). Subject to the same one-in-flight-import concurrency guard as an
    /// ordinary upload.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> ReprocessLatestRetainedAsync(
        string shardId, CloudAssetKind kind, uint adminAccountId, CancellationToken cancellationToken = default) =>
        CloudBoundaryRetry.ExecuteAsync(() => ReprocessLatestRetainedOnceAsync(shardId, kind, adminAccountId, cancellationToken), cancellationToken: cancellationToken);

    private async Task<CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>> ReprocessLatestRetainedOnceAsync(
        string shardId, CloudAssetKind kind, uint adminAccountId, CancellationToken cancellationToken)
    {
        RequireShardId(shardId);

        if (adminAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(adminAccountId));
        }

        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var retained = await _context.Set<CloudRetainedSourceAsset>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.ShardId == shardId && r.Kind == kind, cancellationToken);
        if (retained is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict($"No source DAT has ever been retained for {shardId}/{kind}.");
        }

        var marker = await LockCurrentSessionMarkerAsync(shardId, kind, cancellationToken);
        var existingSession = marker is null
            ? null
            : await _context.Set<CloudAssetImportSession>().SingleAsync(s => s.Id == marker.SessionId, cancellationToken);

        if (existingSession is not null && !CloudAssetImportConcurrencyPolicy.CanStartNewImport(existingSession.State))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Conflict(
                $"An Asset Import for {shardId}/{kind} is already in flight (session {existingSession.Id}).");
        }

        var session = CloudAssetImportSession.CreateForReprocessing(Guid.NewGuid(), shardId, kind, retained.ByteLength, retained.Sha256Hex, adminAccountId);
        _context.Add(session);

        if (marker is null)
        {
            _context.Add(new CloudAssetImportCurrentSessionMarker(shardId, kind, session.Id));
        }
        else
        {
            marker.PointTo(session.Id);
            _context.Update(marker);
        }

        _context.Add(new CloudAssetImportLedgerEvent(
            Guid.NewGuid(), shardId, kind, CloudAssetImportLedgerEventType.ReprocessRequested,
            session.Id, manifestId: null, manifestVersion: null, adminAccountId,
            reason: $"Reprocessing the source DAT retained by session {retained.SourceImportSessionId}."));

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudAssetImportSessionSnapshot>.Committed(CloudAssetImportSessionSnapshot.From(session));
    }

    private async Task<string> AssembleAndHashAsync(Guid sessionId, int chunkCount, CancellationToken cancellationToken)
    {
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var destinationPath = CloudAssetStagingPathPolicy.BuildAssembledUploadRelativePath(sessionId);
        await using (var destination = await _blobStore.OpenWriteAsync(destinationPath, cancellationToken))
        {
            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                await using var source = await _blobStore.OpenReadAsync(
                    CloudAssetStagingPathPolicy.BuildChunkPartRelativePath(sessionId, chunkIndex), cancellationToken);

                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    incrementalHash.AppendData(buffer, 0, bytesRead);
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                }
            }
        }

        return Convert.ToHexStringLower(incrementalHash.GetHashAndReset());
    }

    private async Task<CloudAssetImportSession?> LockSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await _context.Set<CloudAssetImportSession>()
            .FromSqlInterpolated($"SELECT * FROM CloudAssetImportSession WHERE Id = {sessionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudAssetImportCurrentSessionMarker?> LockCurrentSessionMarkerAsync(string shardId, CloudAssetKind kind, CancellationToken cancellationToken) =>
        await _context.Set<CloudAssetImportCurrentSessionMarker>()
            .FromSqlInterpolated($"SELECT * FROM CloudAssetImportCurrentSessionMarker WHERE ShardId = {shardId} AND Kind = {kind.ToString()} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudActiveAssetManifest?> LockActiveManifestPointerAsync(string shardId, CloudAssetKind kind, CancellationToken cancellationToken) =>
        await _context.Set<CloudActiveAssetManifest>()
            .FromSqlInterpolated($"SELECT * FROM CloudActiveAssetManifest WHERE ShardId = {shardId} AND Kind = {kind.ToString()} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudAssetManifest?> LockManifestAsync(string shardId, CloudAssetKind kind, int version, CancellationToken cancellationToken) =>
        await _context.Set<CloudAssetManifest>()
            .FromSqlInterpolated($"SELECT * FROM CloudAssetManifest WHERE ShardId = {shardId} AND Kind = {kind.ToString()} AND Version = {version} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudRetainedSourceAsset?> LockRetainedSourceAsync(string shardId, CloudAssetKind kind, CancellationToken cancellationToken) =>
        await _context.Set<CloudRetainedSourceAsset>()
            .FromSqlInterpolated($"SELECT * FROM CloudRetainedSourceAsset WHERE ShardId = {shardId} AND Kind = {kind.ToString()} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private static void RequireShardId(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An Asset Import operation requires a Cloud Shard ID.", nameof(shardId));
        }
    }

    private async Task<DateTime> GetDatabaseUtcNowAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = _context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = "SELECT UTC_TIMESTAMP(6);";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return DateTime.SpecifyKind(Convert.ToDateTime(result), DateTimeKind.Utc);
    }
}
