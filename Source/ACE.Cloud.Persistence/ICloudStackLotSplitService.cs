namespace ACE.Cloud.Persistence;

/// <summary>
/// The <see cref="CloudStackLotTransactionAuthority.SplitOwnLotAsync"/> capability issue #33's
/// Withdrawal Token creation flow needs for a partial-quantity selection. Deliberately narrower than
/// <see cref="CloudStackLotTransactionAuthority.SplitLotAsync"/> (see that method's own doc
/// comment): this interface only ever splits a lot into a new lot for its own already-verified
/// owner, which is the one shape safe to reach from an authenticated browser-facing endpoint.
/// Interface-extracted for the same reason as <see cref="ICloudAccountOwnershipResolver"/>: so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake instead of standing up a real
/// MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudStackLotSplitService
{
    Task<CloudBoundaryOutcome<CloudStackLotSplitResult>> SplitOwnLotAsync(
        Guid lotId, int expectedVersion, Guid ownerId, int quantityToSplit, CancellationToken cancellationToken = default);
}
