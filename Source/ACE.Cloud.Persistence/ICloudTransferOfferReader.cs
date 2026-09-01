using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>One Transfer Offer summary row for the "sent" or "received" web list (issue #39, XFER-001, XFER-002).</summary>
public sealed record CloudTransferOfferSummary(
    Guid Id,
    Guid SenderAccountId,
    Guid RecipientAccountId,
    CloudTransferOfferStatus Status,
    int Version,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    IReadOnlyList<CloudTransferOfferTargetRecord> Targets);

/// <summary>
/// Interface-extracted (mirroring <see cref="ICloudSharingGrantReader"/>) so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake for the Transfer Offer list
/// endpoint instead of standing up a real MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudTransferOfferReader
{
    /// <summary>Every offer <paramref name="accountId"/>'s effective owner identity currently sent, most recent first.</summary>
    Task<IReadOnlyList<CloudTransferOfferSummary>> GetSentAsync(string shardId, Guid senderOwnerId, CancellationToken cancellationToken = default);

    /// <summary>Every offer <paramref name="accountId"/>'s effective owner identity currently received, most recent first.</summary>
    Task<IReadOnlyList<CloudTransferOfferSummary>> GetReceivedAsync(string shardId, Guid recipientOwnerId, CancellationToken cancellationToken = default);
}

/// <summary>See <see cref="ICloudTransferOfferReader"/>.</summary>
public sealed class CloudTransferOfferReader : ICloudTransferOfferReader
{
    private readonly CloudDbContext _context;

    public CloudTransferOfferReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<IReadOnlyList<CloudTransferOfferSummary>> GetSentAsync(
        string shardId, Guid senderOwnerId, CancellationToken cancellationToken = default) =>
        QueryAsync(shardId, offer => offer.SenderAccountId == senderOwnerId, cancellationToken);

    public Task<IReadOnlyList<CloudTransferOfferSummary>> GetReceivedAsync(
        string shardId, Guid recipientOwnerId, CancellationToken cancellationToken = default) =>
        QueryAsync(shardId, offer => offer.RecipientAccountId == recipientOwnerId, cancellationToken);

    private async Task<IReadOnlyList<CloudTransferOfferSummary>> QueryAsync(
        string shardId, System.Linq.Expressions.Expression<Func<CloudTransferOfferRecord, bool>> predicate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Listing Transfer Offers requires a Cloud Shard ID.", nameof(shardId));
        }

        var offers = await _context.CloudTransferOffers.AsNoTracking()
            .Where(offer => offer.ShardId == shardId)
            .Where(predicate)
            .OrderByDescending(offer => offer.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (offers.Count == 0)
        {
            return [];
        }

        var offerIds = offers.Select(offer => offer.Id).ToList();
        var targetsByOfferId = (await _context.CloudTransferOfferTargets.AsNoTracking()
                .Where(target => offerIds.Contains(target.OfferId))
                .ToListAsync(cancellationToken))
            .GroupBy(target => target.OfferId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CloudTransferOfferTargetRecord>)group.ToList());

        return offers
            .Select(offer => new CloudTransferOfferSummary(
                offer.Id, offer.SenderAccountId, offer.RecipientAccountId, offer.Status, offer.Version,
                offer.CreatedAtUtc, offer.ExpiresAtUtc,
                targetsByOfferId.GetValueOrDefault(offer.Id, [])))
            .ToList();
    }
}
