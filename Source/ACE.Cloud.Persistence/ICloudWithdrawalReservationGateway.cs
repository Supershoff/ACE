using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The Withdrawal Reservation open/cancel/inspect capabilities issue #33's web HTTP surface needs
/// from <see cref="CloudCustodyBoundary"/> (WDR-001, WDR-002, WDR-003), interface-extracted so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake, mirroring
/// <see cref="ICloudAccountOwnershipResolver"/>'s existing precedent. Deliberately narrow: it omits
/// every ace_shard-touching member of <see cref="CloudCustodyBoundary"/> (deposit, redeem, Pyreal
/// conversion) -- those remain ACE-server-only per ARCH-004, while opening/cancelling/previewing a
/// reservation mutates only the ace_cloud schema and is therefore safe for the narrowly privileged
/// companion web identity to call directly.
/// </summary>
public interface ICloudWithdrawalReservationGateway
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

    Task<CloudWithdrawalReservation?> TryGetActiveWithdrawalReservationAsync(
        string tokenHash, CancellationToken cancellationToken = default);

    Task<CloudWithdrawalReservation?> TryGetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudWithdrawalReservationTarget>> GetReservationTargetsAsync(
        Guid reservationId, CancellationToken cancellationToken = default);
}
