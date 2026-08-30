namespace ACE.Cloud.Persistence;

/// <summary>
/// The <see cref="CloudCustodyBoundary"/> Withdrawal Reservation capabilities issue #33's web
/// endpoints need (WDR-001, WDR-002, WDR-003, WDR-006, WDR-008). Interface-extracted for the same
/// reason as <see cref="ICloudAccountOwnershipResolver"/>: so <c>ACE.Cloud.Backend.Tests</c> can
/// substitute an in-memory fake instead of standing up a real MariaDB-backed
/// <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudWithdrawalReservationService
{
    Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> ReserveForWithdrawalAsync(
        IReadOnlyList<CloudWithdrawalReservationRequestTarget> targets,
        string shardId,
        Guid ownerId,
        string tokenHash,
        TimeSpan timeToLive,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> CancelWithdrawalReservationAsync(
        Guid reservationId, int expectedVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudWithdrawalReservationTarget>> GetReservationTargetsAsync(
        Guid reservationId, CancellationToken cancellationToken = default);
}
