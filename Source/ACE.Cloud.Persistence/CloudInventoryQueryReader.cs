using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The authorization-scoped, categorized, paged, versioned typed inventory query (issue #30: "Expose
/// a versioned inventory read API/projection ... produces stable 102-item Mule Pages"). Filtering by
/// shard and by <paramref name="viewer"/>'s authorized owners always happens inside the database
/// query itself, never by fetching every owner's rows and filtering client-side afterward (security
/// baseline: "Search indexes and live streams must be scoped before data leaves the server" -- the
/// same discipline <see cref="CloudLiveStreamReader"/> already applies to the Live State Stream).
/// Identity/ownership/quantity are read from the authoritative <see cref="CloudCustodyRecord"/>/
/// <see cref="CloudStackLot"/> tables (ARCH-005, ARCH-010, docs/adr/0002), not from the disposable
/// <see cref="CloudInventoryReadProjection"/> search cache, so a stackable biota with lots split
/// across several owners is always reported per-lot and always exactly correct; only the
/// category-relevant display properties come from the rebuildable
/// <see cref="CloudInventoryItemPropertiesProjection"/> cache. An item with no captured properties
/// row yet (the future ACE-side property-capture integration has not run for it) does not yet appear
/// in any category query -- it is not lost, only not-yet-indexed, exactly like an item awaiting its
/// first Custody Outbox consumption is not yet in <see cref="CloudInventoryReadProjection"/> either.
/// </summary>
public sealed class CloudInventoryQueryReader
{
    private readonly CloudDbContext _context;

    public CloudInventoryQueryReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CloudInventoryQueryResponse> QueryAsync(
        string shardId,
        CloudLiveStreamViewer viewer,
        CloudInventoryQueryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An inventory query requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(request);

        if (request.Page <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A Mule Page number must be positive.");
        }

        var candidateReader = new CloudInventoryCandidateReader(_context);
        var candidates = await candidateReader.GetAuthorizedCandidatesAsync(shardId, viewer, cancellationToken);

        var page = CloudInventoryQueryEngine.Query(candidates, request.Category, request.Page, request.SortKey, request.SortDirection);
        var asOfSequenceNumber = await candidateReader.GetCustodyProjectionCheckpointAsync(shardId, cancellationToken);

        return new CloudInventoryQueryResponse(page, asOfSequenceNumber);
    }
}
