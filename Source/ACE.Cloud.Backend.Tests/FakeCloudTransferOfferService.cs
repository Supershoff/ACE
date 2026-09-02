using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// An in-memory <see cref="ICloudTransferOfferService"/>/<see cref="ICloudTransferOfferReader"/>
/// substitute that still enforces sender/recipient authorization and version checks, so Transfer
/// Offer endpoint tests exercise real routing/authorization without requiring a real MariaDB (the
/// full transaction/reservation enforcement is proven separately by
/// <c>ACE.Cloud.PersistenceIntegrationTests.CloudTransferOfferGatewayTests</c>).
/// </summary>
internal sealed class FakeCloudTransferOfferService : ICloudTransferOfferService, ICloudTransferOfferReader
{
    private readonly Dictionary<Guid, CloudTransferOfferRecord> _offersById = [];
    private readonly Dictionary<Guid, List<CloudTransferOfferTargetRecord>> _targetsByOfferId = [];

    /// <summary>Maps a typed recipient character name to its owning account's effective owner Guid, mirroring the real gateway's XFER-001 resolution.</summary>
    public Dictionary<string, Guid> RecipientOwnerIdsByCharacterName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> CreateAsync(
        string shardId, uint senderAccountId, string recipientCharacterName, IReadOnlyList<CloudTransferOfferRequestTarget> targets,
        Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (!RecipientOwnerIdsByCharacterName.TryGetValue(recipientCharacterName, out var recipientOwnerId))
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Unknown recipient character '{recipientCharacterName}'."));
        }

        var senderOwnerId = CloudOwnerIdentity.ForAccount(shardId, senderAccountId);
        if (senderOwnerId == recipientOwnerId)
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict("A Transfer Offer cannot target the sender's own account."));
        }

        var nowUtc = DateTime.UtcNow;
        var offer = CloudTransferOfferRecord.Open(
            Guid.NewGuid(), shardId, senderOwnerId, recipientOwnerId, idempotencyKey, nowUtc, nowUtc + CloudTransferOfferGateway.OfferDuration);

        _offersById[offer.Id] = offer;
        _targetsByOfferId[offer.Id] = targets
            .Select(target => target.Kind == CloudWithdrawalReservationTargetKind.Item
                ? CloudTransferOfferTargetRecord.ForItem(offer.Id, target.ItemBiotaId)
                : CloudTransferOfferTargetRecord.ForStackLot(offer.Id, target.StackLotId, quantity: 1))
            .ToList();

        return Task.FromResult(CloudBoundaryOutcome<CloudTransferOfferRecord>.Committed(offer));
    }

    public Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> AcceptAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CancellationToken cancellationToken = default) =>
        ResolveAsync(offerId, actingAccountId, expectedVersion, requireSender: false);

    public Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> DeclineAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CancellationToken cancellationToken = default) =>
        ResolveAsync(offerId, actingAccountId, expectedVersion, requireSender: false);

    public Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> CancelAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, CancellationToken cancellationToken = default) =>
        ResolveAsync(offerId, actingAccountId, expectedVersion, requireSender: true);

    private Task<CloudBoundaryOutcome<CloudTransferOfferRecord>> ResolveAsync(
        Guid offerId, uint actingAccountId, int expectedVersion, bool requireSender)
    {
        if (!_offersById.TryGetValue(offerId, out var offer))
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict($"Transfer Offer {offerId} does not exist."));
        }

        var actingOwnerId = CloudOwnerIdentity.ForAccount(offer.ShardId, actingAccountId);
        var requiredOwnerId = requireSender ? offer.SenderAccountId : offer.RecipientAccountId;
        if (actingOwnerId != requiredOwnerId)
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict("The caller is not authorized to resolve this Transfer Offer."));
        }

        if (offer.Version != expectedVersion)
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudTransferOfferRecord>.Conflict(
                $"Transfer Offer {offerId} is at version {offer.Version}, not the expected version {expectedVersion}."));
        }

        return Task.FromResult(CloudBoundaryOutcome<CloudTransferOfferRecord>.Committed(offer));
    }

    public Task<IReadOnlyList<CloudTransferOfferTargetRecord>> GetTargetsAsync(Guid offerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudTransferOfferTargetRecord>>(_targetsByOfferId.TryGetValue(offerId, out var targets) ? targets : []);

    public Task<IReadOnlyList<CloudTransferOfferSummary>> GetSentAsync(string shardId, Guid senderOwnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudTransferOfferSummary>>(
            _offersById.Values.Where(o => o.ShardId == shardId && o.SenderAccountId == senderOwnerId).Select(ToSummary).ToList());

    public Task<IReadOnlyList<CloudTransferOfferSummary>> GetReceivedAsync(string shardId, Guid recipientOwnerId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudTransferOfferSummary>>(
            _offersById.Values.Where(o => o.ShardId == shardId && o.RecipientAccountId == recipientOwnerId).Select(ToSummary).ToList());

    private CloudTransferOfferSummary ToSummary(CloudTransferOfferRecord offer) => new(
        offer.Id, offer.SenderAccountId, offer.RecipientAccountId, offer.Status, offer.Version, offer.CreatedAtUtc, offer.ExpiresAtUtc,
        _targetsByOfferId.TryGetValue(offer.Id, out var targets) ? targets : []);
}
