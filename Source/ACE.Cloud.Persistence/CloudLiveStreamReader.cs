using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Read-only, authorization-scoped access to the Live State Stream (EVT-007). Filtering happens
/// inside the database query itself, not by fetching everything and filtering client-side, matching
/// the security baseline's "Search indexes and live streams must be scoped before data leaves the
/// server" -- a caller can never accidentally leak an unauthorized private event by forgetting a
/// downstream filter. <see cref="ReadAfterAsync"/>'s cursor argument gives a reconnecting client
/// (a new browser tab, a laptop that just woke up) exactly the same "resume after any gap" guarantee
/// <see cref="CloudCustodyOutboxReader.ReadAfterAsync"/> already gives a backend consumer.
/// </summary>
public sealed class CloudLiveStreamReader
{
    private readonly CloudDbContext _context;

    public CloudLiveStreamReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Returns up to <paramref name="maxCount"/> Live State Stream events strictly after
    /// <paramref name="afterSequenceNumber"/> that <paramref name="viewer"/> is authorized to see
    /// (pass 0 to read from the very beginning, or as the initial full snapshot cursor on first
    /// connect), ordered by <see cref="CloudLiveStreamEvent.SequenceNumber"/> ascending.
    /// </summary>
    public async Task<IReadOnlyList<CloudLiveStreamEvent>> ReadAfterAsync(
        CloudLiveStreamViewer viewer, long afterSequenceNumber, int maxCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (afterSequenceNumber < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(afterSequenceNumber), "A sequence cursor cannot be negative.");
        }

        if (maxCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), "At least one event must be requested.");
        }

        var query = _context.CloudLiveStreamEvents
            .AsNoTracking()
            .Where(evt => evt.SequenceNumber > afterSequenceNumber);

        if (!viewer.IsAdmin)
        {
            var authorizedOwnerIds = viewer.AuthorizedOwnerIds;
            query = query.Where(evt => evt.IsPublic || (evt.ScopeOwnerId != null && authorizedOwnerIds.Contains(evt.ScopeOwnerId.Value)));
        }

        return await query
            .OrderBy(evt => evt.SequenceNumber)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The highest sequence number ever committed, or 0 when the stream is empty.</summary>
    public async Task<long> GetLatestSequenceNumberAsync(CancellationToken cancellationToken = default)
    {
        var hasAny = await _context.CloudLiveStreamEvents.AsNoTracking().AnyAsync(cancellationToken);
        return hasAny
            ? await _context.CloudLiveStreamEvents.AsNoTracking().MaxAsync(evt => evt.SequenceNumber, cancellationToken)
            : 0;
    }
}
