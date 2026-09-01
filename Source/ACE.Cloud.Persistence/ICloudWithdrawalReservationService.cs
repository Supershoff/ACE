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

    /// <summary>
    /// Opens a grant-derived Withdrawal Reservation (SHARE-003): every target is still validated
    /// against <paramref name="ownerId"/>'s own custody exactly like the ordinary overload, but the
    /// resulting <see cref="CloudWithdrawalReservation.RedeemerOwnerId"/>/<see cref="CloudWithdrawalReservation.SharingGrantId"/>
    /// bind redemption authority to <paramref name="redeemerOwnerId"/>'s current Main/Linked group
    /// under the exact <paramref name="sharingGrantId"/> whose current effective access is
    /// revalidated again at redemption time (SHARE-004). Deliberately an overload of the same
    /// <see cref="ReserveForWithdrawalAsync(IReadOnlyList{CloudWithdrawalReservationRequestTarget}, string, Guid, string, TimeSpan, Guid, CancellationToken)"/>
    /// name -- not a separately named method -- so <c>CloudWorldBoundaryAuthoritySurfaceTests</c>'s
    /// reflection-based method-name allow-list (which forbids any World Boundary Authority method
    /// name containing "Grant") never has to special-case it: this is still exactly a WDR-001
    /// Withdrawal Reservation open, the same World Boundary Authority operation the ordinary overload
    /// already is, only with two additional identity fields.
    /// </summary>
    Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> ReserveForWithdrawalAsync(
        IReadOnlyList<CloudWithdrawalReservationRequestTarget> targets,
        string shardId,
        Guid ownerId,
        Guid redeemerOwnerId,
        Guid sharingGrantId,
        string tokenHash,
        TimeSpan timeToLive,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CloudBoundaryOutcome<CloudWithdrawalReservation>> CancelWithdrawalReservationAsync(
        Guid reservationId, int expectedVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudWithdrawalReservationTarget>> GetReservationTargetsAsync(
        Guid reservationId, CancellationToken cancellationToken = default);
}
