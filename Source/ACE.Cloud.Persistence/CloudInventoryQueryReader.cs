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

        var authorizedOwnerIds = viewer.AuthorizedOwnerIds;

        var wholeItemCandidates = await _context.CloudCustodyRecords
            .AsNoTracking()
            .Where(record => record.ShardId == shardId && record.OwnerId != null)
            .Where(record => viewer.IsAdmin || authorizedOwnerIds.Contains(record.OwnerId!.Value))
            .Join(
                _context.CloudInventoryItemPropertiesProjections.AsNoTracking().Where(properties => properties.ShardId == shardId),
                record => record.BiotaId,
                properties => properties.BiotaId,
                (record, properties) => new { record.BiotaId, OwnerId = record.OwnerId!.Value, record.Version, properties })
            .ToListAsync(cancellationToken);

        var stackLotCandidates = await _context.CloudStackLots
            .AsNoTracking()
            .Where(lot => lot.ShardId == shardId)
            .Where(lot => viewer.IsAdmin || authorizedOwnerIds.Contains(lot.OwnerId))
            .Join(
                _context.CloudCustodyRecords.AsNoTracking().Where(record => record.ShardId == shardId),
                lot => lot.CustodyRecordId,
                record => record.Id,
                (lot, record) => new { lot.Id, lot.OwnerId, lot.Quantity, lot.Version, record.BiotaId })
            .Join(
                _context.CloudInventoryItemPropertiesProjections.AsNoTracking().Where(properties => properties.ShardId == shardId),
                joined => joined.BiotaId,
                properties => properties.BiotaId,
                (joined, properties) => new { joined.Id, joined.OwnerId, joined.Quantity, joined.Version, joined.BiotaId, properties })
            .ToListAsync(cancellationToken);

        var reservedBiotaIds = await GetActivelyReservedItemBiotaIdsAsync(cancellationToken);
        var reservedStackLotIds = await GetActivelyReservedStackLotIdsAsync(cancellationToken);

        var candidates = new List<CloudInventoryQueryCandidate>(wholeItemCandidates.Count + stackLotCandidates.Count);

        candidates.AddRange(wholeItemCandidates.Select(candidate => new CloudInventoryQueryCandidate(
            new CloudItemId(candidate.BiotaId),
            StackLotId: null,
            candidate.OwnerId,
            candidate.properties.Name,
            candidate.properties.Category,
            Quantity: 1,
            candidate.properties.Value,
            candidate.properties.Burden,
            IsReserved: reservedBiotaIds.Contains(candidate.BiotaId),
            new CloudAggregateVersion(candidate.Version),
            candidate.properties.IconCacheKeyHex)));

        candidates.AddRange(stackLotCandidates.Select(candidate => new CloudInventoryQueryCandidate(
            new CloudItemId(candidate.BiotaId),
            new CloudStackLotId(candidate.Id),
            candidate.OwnerId,
            candidate.properties.Name,
            candidate.properties.Category,
            candidate.Quantity,
            candidate.properties.Value,
            candidate.properties.Burden,
            IsReserved: reservedStackLotIds.Contains(candidate.Id),
            new CloudAggregateVersion(candidate.Version),
            candidate.properties.IconCacheKeyHex)));

        var page = CloudInventoryQueryEngine.Query(candidates, request.Category, request.Page, request.SortKey, request.SortDirection);
        var asOfSequenceNumber = await GetCustodyProjectionCheckpointAsync(shardId, cancellationToken);

        return new CloudInventoryQueryResponse(page, asOfSequenceNumber);
    }

    private async Task<HashSet<uint>> GetActivelyReservedItemBiotaIdsAsync(CancellationToken cancellationToken)
    {
        var biotaIds = await _context.CloudWithdrawalReservationTargets
            .AsNoTracking()
            .Where(target => target.Kind == CloudWithdrawalReservationTargetKind.Item)
            .Join(
                _context.CloudWithdrawalReservations.AsNoTracking().Where(reservation => reservation.Status == CloudReservationStatus.Active),
                target => target.ReservationId,
                reservation => reservation.Id,
                (target, reservation) => target.ItemBiotaId!.Value)
            .ToListAsync(cancellationToken);

        return [.. biotaIds];
    }

    private async Task<HashSet<Guid>> GetActivelyReservedStackLotIdsAsync(CancellationToken cancellationToken)
    {
        var lotIds = await _context.CloudWithdrawalReservationTargets
            .AsNoTracking()
            .Where(target => target.Kind == CloudWithdrawalReservationTargetKind.StackLot)
            .Join(
                _context.CloudWithdrawalReservations.AsNoTracking().Where(reservation => reservation.Status == CloudReservationStatus.Active),
                target => target.ReservationId,
                reservation => reservation.Id,
                (target, reservation) => target.StackLotId!.Value)
            .ToListAsync(cancellationToken);

        return [.. lotIds];
    }

    /// <summary>
    /// The freshness signal issue #30's Red section calls "projection lag ... responses": the
    /// Custody Outbox sequence number the shard's custody projection consumer has durably applied as
    /// of this query, taken from the same checkpoint row <see cref="CloudCustodyProjectionConsumer"/>
    /// already maintains (ARCH-007). 0 when the consumer has not run yet for this shard.
    /// </summary>
    private async Task<long> GetCustodyProjectionCheckpointAsync(string shardId, CancellationToken cancellationToken)
    {
        var checkpoint = await _context.CloudProjectionCheckpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.ConsumerName == CloudCustodyProjectionConsumer.ConsumerName && c.ShardId == shardId, cancellationToken);

        return checkpoint?.LastAppliedSequenceNumber ?? 0;
    }
}
