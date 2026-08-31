using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Idempotently consumes the Custody Outbox (<see cref="CloudCustodyOutboxReader"/>) into the
/// Notification Center's <see cref="CloudNotification"/> rows (EVT-003), coalescing repeats
/// (<see cref="CloudNotificationCoalescingPolicy"/>) and publishing a private "Notification"
/// <see cref="CloudLiveStreamEvent"/> for every row actually created or updated (EVT-007). Mirrors
/// <see cref="CloudCustodyProjectionConsumer"/>'s exact one-event-per-transaction/checkpoint/
/// dead-letter/rebuild shape -- including its checkpoint-advance-in-the-same-transaction discipline,
/// which is what makes "duplicate outbox delivery does not duplicate notifications" true for free: an
/// outbox event's sequence number is only ever read again by <see cref="RunBatchAsync"/> if it is
/// still above the durably committed checkpoint, so no event is ever applied here twice.
/// </summary>
public sealed class CloudNotificationProjectionConsumer
{
    public const string ConsumerName = "NotificationProjection";

    private readonly CloudDbContext _context;

    public CloudNotificationProjectionConsumer(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<CloudBoundaryOutcome<CloudProjectionRunSummary>> RunBatchAsync(
        string shardId, int maxCount, CancellationToken cancellationToken = default) =>
        RunBatchAsync(shardId, maxCount, poisonInjector: null, cancellationToken);

    /// <summary>Test-only overload; see <see cref="CloudCustodyProjectionConsumer.RunBatchAsync(string, int, Func{CloudProjectionFaultPoint, CloudCustodyOutboxEvent, Exception?}?, CancellationToken)"/>.</summary>
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

    /// <summary>See <see cref="CloudCustodyProjectionConsumer.RebuildAsync"/>.</summary>
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

        var staleNotifications = await _context.CloudNotifications
            .Where(row => row.ShardId == shardId)
            .ToListAsync(cancellationToken);
        _context.CloudNotifications.RemoveRange(staleNotifications);

        var staleDeadLetters = await _context.CloudProjectionDeadLetters
            .Where(entry => entry.ConsumerName == ConsumerName && entry.ShardId == shardId)
            .ToListAsync(cancellationToken);
        _context.CloudProjectionDeadLetters.RemoveRange(staleDeadLetters);

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

            // Not every outbox event is notification-worthy (CloudNotificationClassifier): a
            // self-initiated Deposit/Withdrawal never should be, matching EVT-003's own examples,
            // none of which are "the actor's own just-performed action." The checkpoint still
            // advances past it -- there is nothing else to apply -- so this reuses the same
            // "nothing to do this time" bucket a duplicate/stale delivery would (both leave every
            // row exactly as it already was).
            var isNotificationWorthy = CloudNotificationClassifier.TryClassify(evt.EventType.ToString(), out var kind, out var destination);
            if (!isNotificationWorthy)
            {
                checkpoint.Advance(evt.SequenceNumber);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return CloudProjectionEventOutcomeKind.SkippedAsStale;
            }

            // Looked up regardless of IsRead: a lost/rewound checkpoint (issue #34 Red: "duplicate
            // outbox delivery does not duplicate notifications") can redeliver an event whose
            // notification was already coalesced into and then read. Filtering this lookup to only
            // unread rows would hide that row from the redelivery guard below and let the same
            // already-acknowledged event mint a brand-new spurious unread notification.
            var mostRecentNotification = await _context.CloudNotifications
                .Where(row => row.ShardId == shardId && row.OwnerId == evt.OwnerId && row.Kind == kind)
                .OrderByDescending(row => row.LatestSourceSequenceNumber)
                .FirstOrDefaultAsync(cancellationToken);

            // CloudProjectionSequenceGuard.ShouldApply is the same row-level redelivery guard
            // CloudInventoryReadProjection.TryApply uses, applied here per notification row instead
            // of per biota -- and, unlike the lookup above being IsRead-scoped, now actually catches
            // a redelivery of an event that already applied to a since-read notification.
            if (mostRecentNotification is not null
                && !CloudProjectionSequenceGuard.ShouldApply(mostRecentNotification.LatestSourceSequenceNumber, evt.SequenceNumber))
            {
                checkpoint.Advance(evt.SequenceNumber);
                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return CloudProjectionEventOutcomeKind.SkippedAsStale;
            }

            if (mostRecentNotification is not null
                && CloudNotificationCoalescingPolicy.ShouldCoalesce(mostRecentNotification.Kind, mostRecentNotification.IsRead, kind))
            {
                mostRecentNotification.RecordOccurrence(evt.Id, evt.SequenceNumber);
            }
            else
            {
                _context.CloudNotifications.Add(
                    CloudNotification.CreateFirst(shardId, evt.OwnerId, kind, destination, evt.Id, evt.SequenceNumber));
            }

            var liveStreamSequenceNumber = await CloudLiveStreamSequenceReserver.ReserveNextAsync(_context, cancellationToken);
            _context.CloudLiveStreamEvents.Add(new CloudLiveStreamEvent(
                shardId,
                liveStreamSequenceNumber,
                isPublic: false,
                evt.OwnerId,
                "Notification",
                evt.Id,
                evt.SequenceNumber));

            checkpoint.Advance(evt.SequenceNumber);

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CloudProjectionEventOutcomeKind.Applied;
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
}
