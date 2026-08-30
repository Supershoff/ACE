using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority's off-world half of Cloud Stack Lot handoffs (ADR-0001,
/// ADR-0002): splitting, transferring, and merging quantity claims against a stackable biota
/// already in Cloud custody. Every method here mutates only the ace_cloud schema -- never
/// ace_shard -- and never references a native child GUID, matching ARCH-010's rule that only ACE's
/// world-boundary code (<see cref="CloudCustodyBoundary.WithdrawLotAsync"/>) may materialize one.
/// Split/transfer/merge therefore keep working while the ACE world process is offline (ADR-0002),
/// unlike a withdrawal, which needs ace_shard write access.
///
/// Scope note: unlike <see cref="CloudCustodyBoundary"/>'s Deposit/Withdraw, these methods do not
/// thread an idempotency key through a CloudIdempotencyRecord. That machinery exists to make a
/// world-boundary handoff safe to retry after a crash that leaves the caller unsure whether ACE's
/// process committed; a pure Cloud-schema mutation has no equivalent process-boundary uncertainty
/// in this prototype. Deterministic row locking (this record before its lots, lots in a
/// deterministic order) plus an optimistic expected-version check on the caller-visible lot(s) are
/// enough to make concurrent callers safe (issue #5's Green section: "do not broaden behavior
/// beyond the listed requirements").
/// </summary>
public sealed class CloudStackLotTransactionAuthority : ICloudStackLotSplitGateway
{
    private readonly CloudDbContext _context;

    public CloudStackLotTransactionAuthority(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Carves <paramref name="quantityToSplit"/> off an existing lot into a new lot for
    /// <paramref name="newOwnerId"/>, leaving a positive remainder on the original lot. The sum of
    /// both lots after the split exactly equals the original lot's quantity before it (INV-001).
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudStackLotSplitResult>> SplitLotAsync(
        Guid lotId, int expectedVersion, Guid newOwnerId, int quantityToSplit, CancellationToken cancellationToken = default)
    {
        if (newOwnerId == Guid.Empty)
        {
            throw new ArgumentException("A split requires a target owner.", nameof(newOwnerId));
        }

        if (quantityToSplit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityToSplit), "A split requires a positive quantity.");
        }

        _context.ChangeTracker.Clear();

        var custodyRecordId = await LookUpCustodyRecordIdAsync(lotId, cancellationToken);
        if (custodyRecordId is null)
        {
            return CloudBoundaryOutcome<CloudStackLotSplitResult>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Deterministic lock order (transaction rule 2): the backing stack record before the lot,
        // matching every other stack-mutating operation in this class and in CloudCustodyBoundary.
        await LockCustodyRecordAsync(custodyRecordId.Value, cancellationToken);
        var lot = await LockStackLotAsync(lotId, cancellationToken);

        if (lot is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotSplitResult>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
        }

        if (lot.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotSplitResult>.Conflict(
                $"Cloud Stack Lot {lotId} is at version {lot.Version}, not the expected version {expectedVersion}.");
        }

        if (await HasActiveWithdrawalReservationAsync(lotId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotSplitResult>.Conflict(
                $"Cloud Stack Lot {lotId} has an active Withdrawal Reservation and cannot be split until it is redeemed or cancelled.");
        }

        if (quantityToSplit >= lot.Quantity)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLotSplitResult>.Conflict(
                $"Cannot split {quantityToSplit} from a lot that only has {lot.Quantity}: a split must leave a positive remainder.");
        }

        lot.ReduceQuantity(quantityToSplit);
        var newLot = new CloudStackLot(lot.CustodyRecordId, lot.ShardId, newOwnerId, quantityToSplit);

        _context.CloudStackLots.Update(lot);
        _context.CloudStackLots.Add(newLot);
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudStackLotSplitResult>.Committed(new CloudStackLotSplitResult(lot, newLot));
    }

    /// <summary>
    /// Reassigns a lot to a new owner without changing its quantity.
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudStackLot>> TransferLotAsync(
        Guid lotId, int expectedVersion, Guid newOwnerId, CancellationToken cancellationToken = default)
    {
        if (newOwnerId == Guid.Empty)
        {
            throw new ArgumentException("A transfer requires a target owner.", nameof(newOwnerId));
        }

        _context.ChangeTracker.Clear();

        var custodyRecordId = await LookUpCustodyRecordIdAsync(lotId, cancellationToken);
        if (custodyRecordId is null)
        {
            return CloudBoundaryOutcome<CloudStackLot>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await LockCustodyRecordAsync(custodyRecordId.Value, cancellationToken);
        var lot = await LockStackLotAsync(lotId, cancellationToken);

        if (lot is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLot>.Conflict($"Cloud Stack Lot {lotId} does not exist.");
        }

        if (lot.Version != expectedVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLot>.Conflict(
                $"Cloud Stack Lot {lotId} is at version {lot.Version}, not the expected version {expectedVersion}.");
        }

        if (await HasActiveWithdrawalReservationAsync(lotId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLot>.Conflict(
                $"Cloud Stack Lot {lotId} has an active Withdrawal Reservation and cannot be transferred until it is redeemed or cancelled.");
        }

        lot.ChangeOwner(newOwnerId);
        _context.CloudStackLots.Update(lot);
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudStackLot>.Committed(lot);
    }

    /// <summary>
    /// Merges <paramref name="mergeLotId"/>'s quantity into <paramref name="keepLotId"/> and
    /// removes <paramref name="mergeLotId"/>. Both lots must belong to the same backing stack and
    /// the same owner (ARCH-011: merging is an explicit act, never automatic).
    /// </summary>
    public async Task<CloudBoundaryOutcome<CloudStackLot>> MergeLotsAsync(
        Guid keepLotId, int expectedKeepVersion, Guid mergeLotId, int expectedMergeVersion, CancellationToken cancellationToken = default)
    {
        if (keepLotId == mergeLotId)
        {
            throw new ArgumentException("A lot cannot be merged into itself.", nameof(mergeLotId));
        }

        _context.ChangeTracker.Clear();

        var custodyRecordId = await LookUpCustodyRecordIdAsync(keepLotId, cancellationToken);
        if (custodyRecordId is null)
        {
            return CloudBoundaryOutcome<CloudStackLot>.Conflict($"Cloud Stack Lot {keepLotId} does not exist.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        await LockCustodyRecordAsync(custodyRecordId.Value, cancellationToken);

        // Lock both lots in a deterministic order (ordinal string comparison of their IDs) so two
        // concurrent merges sharing a lot cannot deadlock by acquiring locks in opposite orders.
        var firstId = string.CompareOrdinal(keepLotId.ToString(), mergeLotId.ToString()) <= 0 ? keepLotId : mergeLotId;
        var secondId = firstId == keepLotId ? mergeLotId : keepLotId;

        var first = await LockStackLotAsync(firstId, cancellationToken);
        var second = await LockStackLotAsync(secondId, cancellationToken);

        var keepLot = firstId == keepLotId ? first : second;
        var mergeLot = firstId == keepLotId ? second : first;

        if (keepLot is null || mergeLot is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLot>.Conflict("One or both Cloud Stack Lots to merge do not exist.");
        }

        if (keepLot.CustodyRecordId != mergeLot.CustodyRecordId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLot>.Conflict("Only lots backed by the same stack Cloud Custody Record may be merged.");
        }

        if (keepLot.OwnerId != mergeLot.OwnerId)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLot>.Conflict("Only lots with the same owner may be merged.");
        }

        if (keepLot.Version != expectedKeepVersion || mergeLot.Version != expectedMergeVersion)
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLot>.Conflict("One or both lots are not at their expected version.");
        }

        if (await HasActiveWithdrawalReservationAsync(keepLotId, cancellationToken) ||
            await HasActiveWithdrawalReservationAsync(mergeLotId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return CloudBoundaryOutcome<CloudStackLot>.Conflict(
                "One or both Cloud Stack Lots to merge have an active Withdrawal Reservation and cannot be merged until it is redeemed or cancelled.");
        }

        keepLot.MergeIn(mergeLot.Quantity);
        _context.CloudStackLots.Update(keepLot);
        _context.CloudStackLots.Remove(mergeLot);
        await _context.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return CloudBoundaryOutcome<CloudStackLot>.Committed(keepLot);
    }

    /// <summary>See <see cref="ICloudStackLotSplitGateway.TryGetLotSnapshotAsync"/>.</summary>
    public async Task<CloudStackLotSnapshot?> TryGetLotSnapshotAsync(Guid lotId, CancellationToken cancellationToken = default)
    {
        var lot = await _context.CloudStackLots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == lotId, cancellationToken);
        return lot is null ? null : new CloudStackLotSnapshot(lot.OwnerId, lot.Quantity, lot.Version);
    }

    private async Task<Guid?> LookUpCustodyRecordIdAsync(Guid lotId, CancellationToken cancellationToken) =>
        await _context.CloudStackLots.AsNoTracking()
            .Where(l => l.Id == lotId)
            .Select(l => (Guid?)l.CustodyRecordId)
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// True when <paramref name="lotId"/> is the exclusive target of an active Withdrawal
    /// Reservation (WDR-001/INV-001, issue #122): a reservation is only exclusive if every lot
    /// mutator actually checks it, not just a second reservation attempt
    /// (<see cref="CloudReservationPolicy.Open"/>). Callers must already hold the lot's row lock so
    /// this check and the mutation it guards happen atomically under the same transaction.
    /// </summary>
    private async Task<bool> HasActiveWithdrawalReservationAsync(Guid lotId, CancellationToken cancellationToken) =>
        await (
            from t in _context.CloudWithdrawalReservationTargets
            join r in _context.CloudWithdrawalReservations on t.ReservationId equals r.Id
            where t.StackLotId == lotId && r.Status == CloudReservationStatus.Active
            select t.Id)
            .AnyAsync(cancellationToken);

    private async Task<CloudCustodyRecord?> LockCustodyRecordAsync(Guid custodyRecordId, CancellationToken cancellationToken) =>
        await _context.CloudCustodyRecords
            .FromSqlInterpolated($"SELECT * FROM CloudCustodyRecord WHERE Id = {custodyRecordId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<CloudStackLot?> LockStackLotAsync(Guid lotId, CancellationToken cancellationToken) =>
        await _context.CloudStackLots
            .FromSqlInterpolated($"SELECT * FROM CloudStackLot WHERE Id = {lotId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
}
