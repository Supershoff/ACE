using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudAccountLinkAdministration"/> substitute for endpoint tests.</summary>
internal sealed class FakeCloudAccountLinkAdministration : ICloudAccountLinkAdministration
{
    private readonly Dictionary<uint, Guid> _ownershipGroupIdByMainAccountId = [];
    private readonly Dictionary<uint, List<CloudAccountLinkSummary>> _activeLinksByMainAccountId = [];

    /// <summary>Overrides the next <see cref="LinkAsync"/> call's outcome; defaults to approved.</summary>
    public CloudAccountLinkRejectionCode? NextLinkRejectionCode { get; set; }

    /// <summary>Overrides the next <see cref="UnlinkAsync"/> call's outcome; defaults to approved.</summary>
    public CloudAccountLinkRejectionCode? NextUnlinkRejectionCode { get; set; }

    public uint? LastLinkedSourceAccountId { get; private set; }

    public uint? LastUnlinkedLinkedAccountId { get; private set; }

    public void SeedOwnershipGroup(uint mainAccountId, Guid ownershipGroupId) => _ownershipGroupIdByMainAccountId[mainAccountId] = ownershipGroupId;

    public void SeedActiveLink(uint mainAccountId, uint linkedAccountId, DateTime linkedAtUtc)
    {
        if (!_activeLinksByMainAccountId.TryGetValue(mainAccountId, out var links))
        {
            links = [];
            _activeLinksByMainAccountId[mainAccountId] = links;
        }

        links.Add(new CloudAccountLinkSummary(linkedAccountId, linkedAtUtc));
    }

    public Task<CloudAccountLinkOutcome> LinkAsync(
        string shardId,
        uint mainAccountId,
        uint sourceAccountId,
        Guid idempotencyKey,
        bool wouldCreateActiveAuctionConflict = false,
        CancellationToken cancellationToken = default)
    {
        LastLinkedSourceAccountId = sourceAccountId;

        if (NextLinkRejectionCode is { } rejectionCode)
        {
            return Task.FromResult(CloudAccountLinkOutcome.Rejected(rejectionCode));
        }

        SeedActiveLink(mainAccountId, sourceAccountId, DateTime.UtcNow);
        return Task.FromResult(CloudAccountLinkOutcome.Approved(Guid.NewGuid(), Guid.NewGuid()));
    }

    public Task<CloudAccountLinkOutcome> UnlinkAsync(
        string shardId, uint mainAccountId, uint linkedAccountId, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        LastUnlinkedLinkedAccountId = linkedAccountId;

        if (NextUnlinkRejectionCode is { } rejectionCode)
        {
            return Task.FromResult(CloudAccountLinkOutcome.Rejected(rejectionCode));
        }

        if (_activeLinksByMainAccountId.TryGetValue(mainAccountId, out var links))
        {
            links.RemoveAll(link => link.LinkedAccountId == linkedAccountId);
        }

        return Task.FromResult(CloudAccountLinkOutcome.Approved(Guid.NewGuid(), Guid.NewGuid()));
    }

    public Task<IReadOnlyList<CloudAccountLinkSummary>> GetActiveLinksAsync(
        string shardId, uint mainAccountId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudAccountLinkSummary>>(
            _activeLinksByMainAccountId.TryGetValue(mainAccountId, out var links) ? links : []);

    public Task<Guid?> TryGetOwnershipGroupIdAsync(string shardId, uint mainAccountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_ownershipGroupIdByMainAccountId.TryGetValue(mainAccountId, out var groupId) ? groupId : (Guid?)null);
}
