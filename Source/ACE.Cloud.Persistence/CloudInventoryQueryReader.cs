using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Interface-extracted (mirroring <see cref="ICloudWebSessionStore"/>) so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake for endpoint tests instead of
/// standing up a real MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudInventoryQueryReader
{
    Task<CloudInventoryQueryResponse> QueryAsync(
        string shardId, CloudLiveStreamViewer viewer, CloudInventoryQueryRequest request, CancellationToken cancellationToken = default);

    Task<bool> IsItemVisibleToViewerAsync(
        string shardId, CloudLiveStreamViewer viewer, CloudItemId itemId, CancellationToken cancellationToken = default);
}

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
public sealed class CloudInventoryQueryReader : ICloudInventoryQueryReader
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

    /// <summary>
    /// Issue #31: the authorization check the Full Cloud Appraisal endpoint needs before serving any
    /// item's panel. Reuses the exact same owner/admin rule <see cref="QueryAsync"/> already applies
    /// (never a separate, potentially drifting appraisal-specific rule): visible only when
    /// <paramref name="viewer"/> is an admin, or currently authorized for the whole-item custody
    /// record's owner, or currently authorized for at least one stack lot's owner on this biota.
    /// </summary>
    public async Task<bool> IsItemVisibleToViewerAsync(
        string shardId, CloudLiveStreamViewer viewer, CloudItemId itemId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An item visibility check requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(itemId);

        if (viewer.IsAdmin)
        {
            return await _context.CloudCustodyRecords.AsNoTracking()
                .AnyAsync(record => record.ShardId == shardId && record.BiotaId == itemId.Value, cancellationToken);
        }

        var authorizedOwnerIds = viewer.AuthorizedOwnerIds;

        var wholeItemOwnerVisible = await _context.CloudCustodyRecords.AsNoTracking()
            .AnyAsync(
                record => record.ShardId == shardId && record.BiotaId == itemId.Value
                    && record.OwnerId != null && authorizedOwnerIds.Contains(record.OwnerId!.Value),
                cancellationToken);

        if (wholeItemOwnerVisible)
        {
            return true;
        }

        return await _context.CloudStackLots.AsNoTracking()
            .Join(
                _context.CloudCustodyRecords.AsNoTracking().Where(record => record.ShardId == shardId && record.BiotaId == itemId.Value),
                lot => lot.CustodyRecordId,
                record => record.Id,
                (lot, record) => lot)
            .AnyAsync(lot => lot.ShardId == shardId && authorizedOwnerIds.Contains(lot.OwnerId), cancellationToken);
    }
}
