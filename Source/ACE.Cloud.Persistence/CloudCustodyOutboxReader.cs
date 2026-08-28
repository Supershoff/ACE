using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Read-only access to the durable, ordered Custody Outbox (ARCH-007). This is the interface the
/// companion web service and background workers use to catch up after being offline, and that
/// health/recovery tooling uses to report how far behind consumption is -- it never mutates
/// anything, matching CONTEXT.md's "the web application consumes the Custody Outbox idempotently
/// and can rebuild its searchable read models after an outage." Deposits and withdrawals commit
/// independently of whether anything ever calls this reader (ARCH-007, ARCH-008): every event
/// written by <see cref="CloudCustodyBoundary"/> stays queryable here indefinitely, replayed as
/// many times as a consumer needs.
/// </summary>
public sealed class CloudCustodyOutboxReader
{
    private readonly CloudDbContext _context;

    public CloudCustodyOutboxReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Returns up to <paramref name="maxCount"/> outbox events strictly after
    /// <paramref name="afterSequenceNumber"/> (pass 0 to read from the very beginning), ordered by
    /// <see cref="CloudCustodyOutboxEvent.SequenceNumber"/> ascending. A consumer durably persists
    /// the highest sequence number it has applied and passes that back in as
    /// <paramref name="afterSequenceNumber"/> on its next call, which is enough to resume
    /// deterministically after any restart or outage without losing or duplicating events.
    /// </summary>
    public async Task<IReadOnlyList<CloudCustodyOutboxEvent>> ReadAfterAsync(
        long afterSequenceNumber, int maxCount, CancellationToken cancellationToken = default)
    {
        if (afterSequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterSequenceNumber), "A sequence cursor cannot be negative.");
        }

        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "At least one event must be requested.");
        }

        return await _context.CloudCustodyOutboxEvents
            .AsNoTracking()
            .Where(e => e.SequenceNumber > afterSequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The highest sequence number ever committed, or 0 when the outbox is empty. Diagnostic/health
    /// tooling compares this against a consumer's own last-applied cursor to report how many events
    /// remain unconsumed.
    /// </summary>
    public async Task<long> GetLatestSequenceNumberAsync(CancellationToken cancellationToken = default)
    {
        var hasAny = await _context.CloudCustodyOutboxEvents.AsNoTracking().AnyAsync(cancellationToken);
        return hasAny
            ? await _context.CloudCustodyOutboxEvents.AsNoTracking().MaxAsync(e => e.SequenceNumber, cancellationToken)
            : 0;
    }
}
