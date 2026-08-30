using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// An in-memory <see cref="ICloudWithdrawalReservationService"/> substitute that still enforces
/// target exclusivity (WDR-003) and version-checked cancellation, so Backend endpoint tests exercise
/// real reservation-conflict behavior without requiring a real MariaDB (the full custody-transaction
/// enforcement is proven separately by ACE.Cloud.PersistenceIntegrationTests).
/// </summary>
internal sealed class FakeCloudWithdrawalReservationService : ICloudWithdrawalReservationService, ICloudWithdrawalReservationReader
{
    private readonly Dictionary<Guid, CloudWithdrawalReservation> _reservationsById = [];
    private readonly Dictionary<Guid, List<CloudWithdrawalReservationTarget>> _targetsByReservationId = [];
    private readonly HashSet<string> _activeTargetKeys = [];

    public Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> ReserveForWithdrawalAsync(
        IReadOnlyList<CloudWithdrawalReservationRequestTarget> targets,
        string shardId,
        Guid ownerId,
        string tokenHash,
        TimeSpan timeToLive,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var keys = targets.Select(TargetKey).ToList();
        if (keys.Any(_activeTargetKeys.Contains))
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(
                "One or more requested targets already have an active Withdrawal Reservation."));
        }

        var nowUtc = DateTime.UtcNow;
        var reservation = CloudWithdrawalReservation.Open(shardId, ownerId, tokenHash, idempotencyKey, nowUtc, nowUtc + timeToLive);

        _reservationsById[reservation.Id] = reservation;
        _targetsByReservationId[reservation.Id] = targets
            .Select(target => target.Kind == CloudWithdrawalReservationTargetKind.Item
                ? CloudWithdrawalReservationTarget.ForItem(reservation.Id, target.ItemBiotaId)
                : CloudWithdrawalReservationTarget.ForStackLot(reservation.Id, target.StackLotId, quantity: 1))
            .ToList();

        foreach (var key in keys)
        {
            _activeTargetKeys.Add(key);
        }

        return Task.FromResult(CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(reservation));
    }

    public Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> CancelWithdrawalReservationAsync(
        Guid reservationId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        if (!_reservationsById.TryGetValue(reservationId, out var reservation))
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(
                $"Withdrawal Reservation {reservationId} does not exist."));
        }

        if (reservation.Status == CloudReservationStatus.Released)
        {
            return reservation.ReleaseReason == CloudReservationReleaseReason.Cancelled
                ? Task.FromResult(CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(reservation))
                : Task.FromResult(CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(
                    $"Withdrawal Reservation {reservationId} was already released and cannot be cancelled."));
        }

        if (reservation.Version != expectedVersion)
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict(
                $"Withdrawal Reservation {reservationId} is at version {reservation.Version}, not the expected version {expectedVersion}."));
        }

        reservation.Release(DateTime.UtcNow, CloudReservationReleaseReason.Cancelled);

        foreach (var target in _targetsByReservationId[reservationId])
        {
            _activeTargetKeys.Remove(TargetKey(target));
        }

        return Task.FromResult(CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(reservation));
    }

    public Task<CloudWithdrawalReservation?> TryGetActiveByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_reservationsById.Values
            .Where(r => r.OwnerId == ownerId && r.Status == CloudReservationStatus.Active)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefault());

    public Task<IReadOnlyList<CloudWithdrawalReservationTarget>> GetReservationTargetsAsync(
        Guid reservationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudWithdrawalReservationTarget>>(
            _targetsByReservationId.TryGetValue(reservationId, out var targets) ? targets : []);

    private static string TargetKey(CloudWithdrawalReservationRequestTarget target) =>
        target.Kind == CloudWithdrawalReservationTargetKind.Item ? $"item:{target.ItemBiotaId}" : $"lot:{target.StackLotId}";

    private static string TargetKey(CloudWithdrawalReservationTarget target) =>
        target.Kind == CloudWithdrawalReservationTargetKind.Item ? $"item:{target.ItemBiotaId}" : $"lot:{target.StackLotId}";
}
