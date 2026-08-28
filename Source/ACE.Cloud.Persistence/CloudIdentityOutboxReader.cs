using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Read-only access to the durable, ordered identity/allegiance outbox (ARCH-007 applied to
/// AUTH-003/VAULT-001), the exact same "catch up after any outage" contract
/// <see cref="CloudCustodyOutboxReader"/> provides for custody handoffs. Never mutates anything;
/// every event stays queryable here indefinitely, replayed as many times as a consumer needs.
/// </summary>
public sealed class CloudIdentityOutboxReader
{
    private readonly CloudDbContext _context;

    public CloudIdentityOutboxReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Returns up to <paramref name="maxCount"/> identity/allegiance events strictly after
    /// <paramref name="afterSequenceNumber"/> (pass 0 to read from the very beginning), ordered by
    /// <see cref="CloudIdentityOutboxEvent.SequenceNumber"/> ascending. A consumer durably persists
    /// the highest sequence number it has applied and passes that back in on its next call, which is
    /// enough to resume deterministically after any restart or outage without losing or duplicating
    /// events.
    /// </summary>
    public async Task<IReadOnlyList<CloudIdentityOutboxEvent>> ReadAfterAsync(
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

        return await _context.CloudIdentityOutboxEvents
            .AsNoTracking()
            .Where(e => e.SequenceNumber > afterSequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The highest sequence number ever committed, or 0 when the outbox is empty.
    /// </summary>
    public async Task<long> GetLatestSequenceNumberAsync(CancellationToken cancellationToken = default)
    {
        var hasAny = await _context.CloudIdentityOutboxEvents.AsNoTracking().AnyAsync(cancellationToken);
        return hasAny
            ? await _context.CloudIdentityOutboxEvents.AsNoTracking().MaxAsync(e => e.SequenceNumber, cancellationToken)
            : 0;
    }
}
