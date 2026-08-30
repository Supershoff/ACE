namespace ACE.Cloud.Persistence;

/// <summary>
/// The web-safe entry point for opening, cancelling, and inspecting a Withdrawal Reservation
/// (WDR-001, WDR-002, WDR-003), used by ACE.Cloud.Backend through <see cref="ICloudWithdrawalReservationGateway"/>.
///
/// <see cref="CloudCustodyBoundary"/>'s own doc comment states "the narrowly privileged companion
/// web identity (ARCH-004) must never be given this class" -- true even though the four methods
/// delegated to here only ever mutate the ace_cloud schema, because the *class* also exposes
/// ace_shard-privileged deposit/withdraw/redeem members a caller holding the object itself could
/// reach. Rather than duplicating <see cref="CloudCustodyBoundary"/>'s carefully reasoned locking
/// and idempotency logic (issue #33's Refactor bullet: "search adjacent Cloud code for an existing
/// helper/policy before accepting duplication"), this wrapper constructs its own private
/// <see cref="CloudCustodyBoundary"/> instance and never lets it escape past these four narrow,
/// interface-declared members -- so the Cloud backend's dependency injection container only ever
/// resolves <see cref="ICloudWithdrawalReservationGateway"/>, never <see cref="CloudCustodyBoundary"/>
/// itself. WDR-008 ("if ACE is down, block token creation and redemption") is enforced by the HTTP
/// endpoint checking world-boundary health before calling this gateway at all, not by anything in
/// this class -- opening/cancelling a reservation touches only ace_cloud and would otherwise succeed
/// even while the ACE world process is offline.
/// </summary>
public sealed class CloudWithdrawalReservationGateway : ICloudWithdrawalReservationGateway
{
    private readonly CloudCustodyBoundary _boundary;

    public CloudWithdrawalReservationGateway(CloudDbContext context)
    {
        _boundary = new CloudCustodyBoundary(context);
    }

    public Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> ReserveForWithdrawalAsync(
        IReadOnlyList<CloudWithdrawalReservationRequestTarget> targets,
        string shardId,
        Guid ownerId,
        string tokenHash,
        TimeSpan timeToLive,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default) =>
        _boundary.ReserveForWithdrawalAsync(targets, shardId, ownerId, tokenHash, timeToLive, idempotencyKey, cancellationToken);

    public Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> CancelWithdrawalReservationAsync(
        Guid reservationId, int expectedVersion, CancellationToken cancellationToken = default) =>
        _boundary.CancelWithdrawalReservationAsync(reservationId, expectedVersion, cancellationToken);

    public Task<CloudWithdrawalReservation?> TryGetActiveWithdrawalReservationAsync(
        string tokenHash, CancellationToken cancellationToken = default) =>
        _boundary.TryGetActiveWithdrawalReservationAsync(tokenHash, cancellationToken);

    public Task<CloudWithdrawalReservation?> TryGetReservationAsync(Guid reservationId, CancellationToken cancellationToken = default) =>
        _boundary.TryGetReservationAsync(reservationId, cancellationToken);

    public Task<IReadOnlyList<CloudWithdrawalReservationTarget>> GetReservationTargetsAsync(
        Guid reservationId, CancellationToken cancellationToken = default) =>
        _boundary.GetReservationTargetsAsync(reservationId, cancellationToken);
}
