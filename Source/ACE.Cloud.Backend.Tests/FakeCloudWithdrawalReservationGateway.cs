using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudWithdrawalReservationGateway"/> substitute.</summary>
internal sealed class FakeCloudWithdrawalReservationGateway : ICloudWithdrawalReservationGateway
{
    private readonly Dictionary<Guid, CloudWithdrawalReservation> _reservationsById = [];

    public CloudBoundaryOutcome<CloudWithdrawalReservation>? NextReserveOutcome { get; set; }

    public CloudBoundaryOutcome<CloudWithdrawalReservation>? NextCancelOutcome { get; set; }

    public List<IReadOnlyList<CloudWithdrawalReservationRequestTarget>> ReserveCalls { get; } = [];

    public void Seed(CloudWithdrawalReservation reservation) => _reservationsById[reservation.Id] = reservation;

    public Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> ReserveForWithdrawalAsync(
        IReadOnlyList<CloudWithdrawalReservationRequestTarget> targets,
        string shardId,
        Guid ownerId,
        string tokenHash,
        TimeSpan timeToLive,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ReserveCalls.Add(targets);

        var outcome = NextReserveOutcome ?? CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(
            CloudWithdrawalReservation.Open(shardId, ownerId, tokenHash, idempotencyKey, DateTime.UtcNow, DateTime.UtcNow + timeToLive));

        if (outcome.Kind == CloudBoundaryOutcomeKind.Committed)
        {
            _reservationsById[outcome.Value!.Id] = outcome.Value;
        }

        return Task.FromResult(outcome);
    }

    public Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> CancelWithdrawalReservationAsync(
        Guid reservationId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        if (NextCancelOutcome is not null)
        {
            return Task.FromResult(NextCancelOutcome);
        }

        if (!_reservationsById.TryGetValue(reservationId, out var reservation))
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict($"Withdrawal Reservation {reservationId} does not exist."));
        }

        return Task.FromResult(CloudBoundaryOutcome<CloudWithdrawalReservation>.Committed(reservation));
    }

    public Task<CloudWithdrawalReservation?> TryGetActiveWithdrawalReservationAsync(
        string tokenHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_reservationsById.Values.SingleOrDefault(r => r.TokenHash == tokenHash && r.Status == CloudReservationStatus.Active));

    public Task<CloudWithdrawalReservation?> TryGetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_reservationsById.GetValueOrDefault(reservationId));

    public Task<IReadOnlyList<CloudWithdrawalReservationTarget>> GetReservationTargetsAsync(
        Guid reservationId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudWithdrawalReservationTarget>>([]);
}
