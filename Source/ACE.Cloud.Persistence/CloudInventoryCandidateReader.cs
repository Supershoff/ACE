using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The authorization-scoped candidate fetch shared by <see cref="CloudInventoryQueryReader"/> (issue
/// #30) and <see cref="CloudInventorySearchReader"/> (issue #32), extracted so the same
/// database-side authorization scoping (security baseline: "Search indexes and live streams must be
/// scoped before data leaves the server") is written and reviewed exactly once rather than drifting
/// between two nearly-identical queries. Neither caller fetches a row it was not already authorized
/// to see; category/text/property/regex narrowing all happen afterward, in memory, over what this
/// type already scoped.
/// </summary>
internal sealed class CloudInventoryCandidateReader
{
    private readonly CloudDbContext _context;

    public CloudInventoryCandidateReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<CloudInventoryQueryCandidate>> GetAuthorizedCandidatesAsync(
        string shardId, CloudLiveStreamViewer viewer, CancellationToken cancellationToken)
    {
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

        return candidates;
    }

    /// <summary>
    /// The freshness signal issue #30's Red section calls "projection lag ... responses": the
    /// Custody Outbox sequence number the shard's custody projection consumer has durably applied as
    /// of this query, taken from the same checkpoint row <see cref="CloudCustodyProjectionConsumer"/>
    /// already maintains (ARCH-007). 0 when the consumer has not run yet for this shard.
    /// </summary>
    public async Task<long> GetCustodyProjectionCheckpointAsync(string shardId, CancellationToken cancellationToken)
    {
        var checkpoint = await _context.CloudProjectionCheckpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.ConsumerName == CloudCustodyProjectionConsumer.ConsumerName && c.ShardId == shardId, cancellationToken);

        return checkpoint?.LastAppliedSequenceNumber ?? 0;
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
}
