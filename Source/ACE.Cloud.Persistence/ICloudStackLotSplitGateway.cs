namespace ACE.Cloud.Persistence;

/// <summary>
/// The one <see cref="CloudStackLotTransactionAuthority"/> capability issue #33's withdrawal-open
/// endpoint needs (INV-002's partial-quantity selection: split off exactly the requested quantity
/// into a new lot, then reserve that new lot in full), interface-extracted so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake, mirroring
/// <see cref="ICloudAccountOwnershipResolver"/>'s existing precedent. Unlike
/// <see cref="ICloudWithdrawalReservationGateway"/>, no wrapper indirection is needed here:
/// <see cref="CloudStackLotTransactionAuthority"/>'s own doc comment already states its methods
/// "mutate only the ace_cloud schema -- never ace_shard," so it carries none of
/// <see cref="CloudCustodyBoundary"/>'s "must never be given this class" restriction.
/// </summary>
public interface ICloudStackLotSplitGateway
{
    Task<CloudBoundaryOutcome<CloudStackLotSplitResult>> SplitLotAsync(
        Guid lotId, int expectedVersion, Guid newOwnerId, int quantityToSplit, CancellationToken cancellationToken = default);

    /// <summary>
    /// A plain (unlocked) ownership/quantity/version read a caller must use to authorize a split
    /// request server-side (security baseline: "Authorization is server-side on every object query
    /// and command") before calling <see cref="SplitLotAsync"/>, which -- like every
    /// <see cref="CloudStackLotTransactionAuthority"/> method -- trusts its caller to have already
    /// resolved that authorization rather than re-deriving it itself. Returns null if the lot does
    /// not exist.
    /// </summary>
    Task<CloudStackLotSnapshot?> TryGetLotSnapshotAsync(Guid lotId, CancellationToken cancellationToken = default);
}

/// <summary>See <see cref="ICloudStackLotSplitGateway.TryGetLotSnapshotAsync"/>.</summary>
public sealed record CloudStackLotSnapshot(Guid OwnerId, int Quantity, int Version);
