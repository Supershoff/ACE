using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Idempotently consumes the identity/allegiance outbox (<see cref="CloudIdentityOutboxReader"/>)
/// into <see cref="CloudCharacterIdentityReadProjection"/>, the "versioned/refreshed from ACE" cache
/// CONTEXT.md permits for AUTH-003/VAULT-001. Mirrors <see cref="CloudCustodyProjectionConsumer"/>'s
/// one-event-per-transaction/checkpoint/dead-letter/rebuild shape exactly, with one deliberate
/// difference: it never publishes to the Live State Stream. EVT-007 enumerates the change kinds that
/// stream propagates -- inventory, reservation, bid, listing, offer, notification -- and a bare
/// identity event here carries only an ACE account ID, not the Cloud Account (Main Account) Guid the
/// stream's authorization scope requires; deriving that mapping belongs to the account-linking
/// module, not to this consumer.
/// </summary>
public sealed class CloudIdentityProjectionConsumer
{
    public const string ConsumerName = "IdentityProjection";

    private readonly CloudDbContext _context;

    public CloudIdentityProjectionConsumer(CloudDbContext context)
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
        Func<CloudProjectionFaultPoint, CloudIdentityOutboxEvent, Exception?>? poisonInjector,
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

            var events = await new CloudIdentityOutboxReader(_context)
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

        var staleProjections = await _context.CloudCharacterIdentityReadProjections
            .Where(row => row.ShardId == shardId)
            .ToListAsync(cancellationToken);
        _context.CloudCharacterIdentityReadProjections.RemoveRange(staleProjections);

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
        CloudIdentityOutboxEvent evt,
        Func<CloudProjectionFaultPoint, CloudIdentityOutboxEvent, Exception?>? poisonInjector,
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

            var current = await _context.CloudCharacterIdentityReadProjections
                .SingleOrDefaultAsync(row => row.CharacterId == evt.CharacterId, cancellationToken);

            var (row, applied) = CloudCharacterIdentityReadProjection.TryApply(current, evt);

            if (applied && current is null)
            {
                _context.CloudCharacterIdentityReadProjections.Add(row);
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
}
