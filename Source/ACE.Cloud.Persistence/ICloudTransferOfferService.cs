namespace ACE.Cloud.Persistence;

/// <summary>
/// The <see cref="CloudTransferOfferGateway"/> mutation capabilities issue #39's Transfer Offer web
/// endpoints need (XFER-001, XFER-002). Interface-extracted for the same reason as
/// <see cref="ICloudWithdrawalReservationService"/>: so <c>ACE.Cloud.Backend.Tests</c> can substitute
/// an in-memory fake instead of standing up a real MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudTransferOfferService
{
    Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> CreateAsync(
        string shardId,
        uint senderAccountId,
        string recipientCharacterName,
        IReadOnlyList<CloudTransferOfferRequestTarget> targets,
        Guid idempotencyKey,
        CancellationToken cancellationToken = default);

    Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> AcceptAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CancellationToken cancellationToken = default);

    Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> DeclineAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CancellationToken cancellationToken = default);

    Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> CancelAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CloudTransferOfferTargetRecord>> GetTargetsAsync(Guid offerId, CancellationToken cancellationToken = default);
}
