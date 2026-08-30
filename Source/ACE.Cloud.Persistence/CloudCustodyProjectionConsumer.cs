using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Idempotently consumes the Custody Outbox (<see cref="CloudCustodyOutboxReader"/>) into
/// <see cref="CloudInventoryReadProjection"/> and, for every event actually applied (never for a
/// duplicate/stale one), an authorization-scoped <see cref="CloudLiveStreamEvent"/> (ARCH-007,
/// EVT-007). Processes one event per database transaction so a crash between events leaves
/// <see cref="CloudProjectionCheckpoint"/> pointing exactly at the last event durably applied --
/// resuming after a restart or after the outbox is completely empty are the same code path, which is
/// what makes <see cref="RebuildAsync"/> (wipe the projection and checkpoint, then drain the outbox
/// from the beginning) reproduce the exact same query state ordinary incremental consumption would
/// have reached.
/// </summary>
public sealed class CloudCustodyProjectionConsumer
{
    public const string ConsumerName = "CustodyProjection";

    private readonly CloudDbContext _context;

    public CloudCustodyProjectionConsumer(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Applies up to <paramref name="maxCount"/> not-yet-applied Custody Outbox events. Returns
    /// <see cref="CloudBoundaryOutcomeKind.Unavailable"/> rather than throwing when the database is
    /// unreachable (ARCH-009): a poller sees this and simply retries on its next tick without ever
    /// queuing work for later replay.
    /// </summary>
    public Task<CloudBoundaryOutcome<CloudProjectionRunSummary>> RunBatchAsync(
        string shardId, int maxCount, CancellationToken cancellationToken = default) =>
        RunBatchAsync(shardId, maxCount, poisonInjector: null, cancellationToken);

    /// <summary>
    /// Test-only overload: <paramref name="poisonInjector"/>, when it returns a non-null exception
    /// for a given event, simulates that event being unprocessable so Red tests can deterministically
    /// exercise the dead-letter path without needing to construct a naturally-malformed event.
    /// Production callers always use the public overload, which passes null.
    /// </summary>
    internal async Task<CloudBoundaryOutcome<CloudProjectionRunSummary>> RunBatchAsync(
        string shardId,
        int maxCount,
        Func<CloudProjectionFaultPoint, CloudCustodyOutboxEvent, Exception?>? poisonInjector,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A projection consumer requires a Cloud Shard ID.", nameof(shardId));
        }

        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "At least one event must be requested.");
        }

        try
        {
            await EnsureCheckpointAsync(shardId, cancellationToken);

            var checkpoint = await _context.CloudProjectionCheckpoints.AsNoTracking()
                .SingleAsync(c => c.ConsumerName == ConsumerName, cancellationToken);

            var events = await new CloudCustodyOutboxReader(_context)
                .ReadAfterAsync(checkpoint.LastAppliedSequenceNumber, maxCount, cancellationToken);

            var applied = 0;
            var skipped = 0;
            var deadLettered = 0;

            foreach (var evt in events)
            {
                switch (await ApplyOneEventAsync(shardId, evt, poisonInjector, cancellationToken))
                {
                    case CloudProjectionEventOutcomeKind.Applied:
                        applied++;
                        break;
                    case CloudProjectionEventOutcomeKind.SkippedAsStale:
                        skipped++;
                        break;
                    case CloudProjectionEventOutcomeKind.DeadLettered:
                        deadLettered++;
                        break;
                }
            }

            return CloudBoundaryOutcome<CloudProjectionRunSummary>.Committed(
                new CloudProjectionRunSummary(events.Count, applied, skipped, deadLettered));
        }
        catch (Exception ex) when (CloudBoundaryRetry.IsUnavailable(ex))
        {
            var mySqlException = CloudBoundaryRetry.UnwrapMySqlException(ex)!;
            return CloudBoundaryOutcome<CloudProjectionRunSummary>.Unavailable(
                $"The Cloud schema database is unavailable: {mySqlException.Message}");
        }
    }

    /// <summary>
    /// Wipes this consumer's projection rows, dead letters, Live State Stream events, and checkpoint
    /// for <paramref name="shardId"/>, then drains the Custody Outbox from the very beginning in
    /// batches of <paramref name="batchSize"/> until caught up (issue #22's Green "full rebuild
    /// commands"). Because incremental consumption and this rebuild both apply the exact same
    /// per-event logic, the resulting projection state is identical to what incremental consumption
    /// alone would have produced -- and re-deriving the stream from scratch (rather than leaving old
    /// entries in place) means a rebuild never republishes a duplicate entry for an event that was
    /// already streamed once.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudProjectionRunSummary>> RebuildAsync(
        string shardId, int batchSize = 500, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A projection consumer requires a Cloud Shard ID.", nameof(shardId));
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "At least one event must be requested per batch.");
        }

        try
        {
            await ResetForRebuildAsync(shardId, cancellationToken);

            var totalRead = 0;
            var totalApplied = 0;
            var totalSkipped = 0;
            var totalDeadLettered = 0;

            while (true)
            {
                var batchOutcome = await RunBatchAsync(shardId, batchSize, poisonInjector: null, cancellationToken);
                if (batchOutcome.Kind != CloudBoundaryOutcomeKind.Committed)
                {
                    return batchOutcome;
                }

                totalRead += batchOutcome.Value!.EventsRead;
                totalApplied += batchOutcome.Value.EventsApplied;
                totalSkipped += batchOutcome.Value.EventsSkippedAsStale;
                totalDeadLettered += batchOutcome.Value.EventsDeadLettered;

                if (batchOutcome.Value.CaughtUp)
                {
                    break;
                }
            }

            return CloudBoundaryOutcome<CloudProjectionRunSummary>.Committed(
                new CloudProjectionRunSummary(totalRead, totalApplied, totalSkipped, totalDeadLettered));
        }
        catch (Exception ex) when (CloudBoundaryRetry.IsUnavailable(ex))
        {
            var mySqlException = CloudBoundaryRetry.UnwrapMySqlException(ex)!;
            return CloudBoundaryOutcome<CloudProjectionRunSummary>.Unavailable(
                $"The Cloud schema database is unavailable: {mySqlException.Message}");
        }
    }

    private async Task ResetForRebuildAsync(string shardId, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var staleProjections = await _context.CloudInventoryReadProjections
            .Where(row => row.ShardId == shardId)
            .ToListAsync(cancellationToken);
        _context.CloudInventoryReadProjections.RemoveRange(staleProjections);

        var staleDeadLetters = await _context.CloudProjectionDeadLetters
            .Where(entry => entry.ConsumerName == ConsumerName && entry.ShardId == shardId)
            .ToListAsync(cancellationToken);
        _context.CloudProjectionDeadLetters.RemoveRange(staleDeadLetters);

        var staleLiveStreamEvents = await _context.CloudLiveStreamEvents
            .Where(evt => evt.ShardId == shardId)
            .ToListAsync(cancellationToken);
        _context.CloudLiveStreamEvents.RemoveRange(staleLiveStreamEvents);

        var checkpoint = await _context.CloudProjectionCheckpoints
            .SingleOrDefaultAsync(c => c.ConsumerName == ConsumerName, cancellationToken);
        if (checkpoint is null)
        {
            _context.CloudProjectionCheckpoints.Add(new CloudProjectionCheckpoint(ConsumerName, shardId));
        }
        else
        {
            checkpoint.ResetForRebuild();
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    private async Task EnsureCheckpointAsync(string shardId, CancellationToken cancellationToken)
    {
        var exists = await _context.CloudProjectionCheckpoints.AsNoTracking()
            .AnyAsync(c => c.ConsumerName == ConsumerName, cancellationToken);
        if (exists)
        {
            return;
        }

        _context.CloudProjectionCheckpoints.Add(new CloudProjectionCheckpoint(ConsumerName, shardId));
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    private async Task<CloudProjectionEventOutcomeKind> ApplyOneEventAsync(
        string shardId,
        CloudCustodyOutboxEvent evt,
        Func<CloudProjectionFaultPoint, CloudCustodyOutboxEvent, Exception?>? poisonInjector,
        CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var simulatedFailure = poisonInjector?.Invoke(CloudProjectionFaultPoint.BeforeApply, evt);
            if (simulatedFailure is not null)
            {
                throw simulatedFailure;
            }

            var checkpoint = await _context.CloudProjectionCheckpoints
                .SingleAsync(c => c.ConsumerName == ConsumerName, cancellationToken);

            var current = await _context.CloudInventoryReadProjections
                .SingleOrDefaultAsync(row => row.BiotaId == evt.BiotaId, cancellationToken);

            var (row, applied) = CloudInventoryReadProjection.TryApply(
                current, evt.BiotaId, evt.ShardId, evt.OwnerId, evt.EventType, evt.SequenceNumber);

            if (applied)
            {
                if (current is null)
                {
                    _context.CloudInventoryReadProjections.Add(row);
                }

                var liveStreamSequenceNumber = await ReserveNextLiveStreamSequenceNumberAsync(cancellationToken);
                _context.CloudLiveStreamEvents.Add(new CloudLiveStreamEvent(
                    shardId,
                    liveStreamSequenceNumber,
                    isPublic: false,
                    evt.OwnerId,
                    evt.EventType.ToString(),
                    evt.Id,
                    evt.SequenceNumber));
            }

            checkpoint.Advance(evt.SequenceNumber);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return applied ? CloudProjectionEventOutcomeKind.Applied : CloudProjectionEventOutcomeKind.SkippedAsStale;
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            && !CloudBoundaryRetry.IsUnavailable(ex)
            && !CloudBoundaryRetry.IsDeadlockOrLockTimeout(ex))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _context.ChangeTracker.Clear();

            var checkpoint = await _context.CloudProjectionCheckpoints
                .SingleAsync(c => c.ConsumerName == ConsumerName, cancellationToken);
            checkpoint.Advance(evt.SequenceNumber);
            _context.CloudProjectionDeadLetters.Add(new CloudProjectionDeadLetter(
                ConsumerName, shardId, evt.Id, evt.SequenceNumber, CloudProjectionFailureDescriber.Describe(ex)));

            await _context.SaveChangesAsync(cancellationToken);
            return CloudProjectionEventOutcomeKind.DeadLettered;
        }
    }

    /// <summary>
    /// Locks <see cref="CloudLiveStreamSequence"/>'s single row and returns the next durable order
    /// position, the same pattern <c>CloudCustodyBoundary.ReserveNextOutboxSequenceNumberAsync</c>
    /// uses for the Custody Outbox itself.
    /// </summary>
    private async Task<long> ReserveNextLiveStreamSequenceNumberAsync(CancellationToken cancellationToken)
    {
        var connection = _context.Database.GetDbConnection();
        var transaction = _context.Database.CurrentTransaction?.GetDbTransaction();

        long reserved;
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT NextValue FROM CloudLiveStreamSequence WHERE Id = 1 FOR UPDATE;";
            reserved = Convert.ToInt64(await read.ExecuteScalarAsync(cancellationToken));
        }

        await using (var update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = "UPDATE CloudLiveStreamSequence SET NextValue = @nextValue WHERE Id = 1;";
            var parameter = update.CreateParameter();
            parameter.ParameterName = "@nextValue";
            parameter.Value = reserved + 1;
            update.Parameters.Add(parameter);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        return reserved;
    }
}
