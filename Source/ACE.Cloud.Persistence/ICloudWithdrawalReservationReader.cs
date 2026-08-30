namespace ACE.Cloud.Persistence;

/// <summary>
/// The Cloud Transaction Authority-side read issue #33's web "current withdrawal" status view needs
/// (WDR-001, EVT-007): unlike every other Withdrawal Reservation operation, looking up "the owner's
/// current active reservation" is a pure Cloud-schema read with no native-biota or World Boundary
/// Authority involvement at all, so it deliberately lives here rather than as a
/// <see cref="CloudCustodyBoundary"/> method (see <c>CloudWorldBoundaryAuthoritySurfaceTests</c>,
/// ARCH-002/ARCH-003: that gateway's public surface is allow-listed precisely to keep Cloud-only
/// reads like this one out of it).
/// </summary>
public interface ICloudWithdrawalReservationReader
{
    /// <summary>The owner's single active Withdrawal Reservation, if any -- used to reconcile the web UI's current-withdrawal view without ever needing the Withdrawal Token secret again after issuance.</summary>
    Task<CloudWithdrawalReservation?> TryGetActiveByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);
}
