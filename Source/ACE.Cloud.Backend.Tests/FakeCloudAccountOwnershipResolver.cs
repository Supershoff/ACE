using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// An in-memory <see cref="ICloudAccountLinkGateway"/> substitute, defaulting every account to
/// standalone (its own effective owner) unless a test configures a link. Test-controllable outcomes
/// let endpoint tests exercise <c>AccountEndpoints</c>'s own HTTP-layer logic (origin/auth/rate-limit
/// checks, request validation, outcome-to-status-code mapping) without re-proving
/// <c>CloudAccountLinkGateway</c>'s own transactional policy, which
/// ACE.Cloud.Domain.Tests/ACE.Cloud.PersistenceIntegrationTests already cover.
/// </summary>
internal sealed class FakeCloudAccountOwnershipResolver : ICloudAccountLinkGateway
{
    private readonly Dictionary<uint, uint> _effectiveMainAccountIdByAccountId = [];
    private readonly Dictionary<uint, Guid> _groupIdByMainAccountId = [];
    private readonly Dictionary<uint, HashSet<uint>> _groupAccountIdsByMainAccountId = [];

    public void SetLinked(uint accountId, uint mainAccountId) => _effectiveMainAccountIdByAccountId[accountId] = mainAccountId;

    public void SetOwnershipGroup(uint mainAccountId, Guid groupId, params uint[] groupAccountIds)
    {
        _groupIdByMainAccountId[mainAccountId] = groupId;
        _groupAccountIdsByMainAccountId[mainAccountId] = [.. groupAccountIds, mainAccountId];
    }

    public CloudAccountLinkOutcome NextLinkOutcome { get; set; } = CloudAccountLinkOutcome.Approved(Guid.NewGuid(), Guid.NewGuid());

    public CloudAccountLinkOutcome NextUnlinkOutcome { get; set; } = CloudAccountLinkOutcome.Approved(Guid.NewGuid(), Guid.NewGuid());

    public List<(uint MainAccountId, uint SourceAccountId)> LinkCalls { get; } = [];

    public List<(uint MainAccountId, uint LinkedAccountId)> UnlinkCalls { get; } = [];

    public Task<uint> ResolveEffectiveOwnerAccountIdAsync(string shardId, uint accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_effectiveMainAccountIdByAccountId.GetValueOrDefault(accountId, accountId));

    public Task<CloudAccountLinkOutcome> LinkAsync(
        string shardId, uint mainAccountId, uint sourceAccountId, Guid idempotencyKey,
        bool wouldCreateActiveAuctionConflict = false, CancellationToken cancellationToken = default)
    {
        LinkCalls.Add((mainAccountId, sourceAccountId));
        return Task.FromResult(NextLinkOutcome);
    }

    public Task<CloudAccountLinkOutcome> UnlinkAsync(
        string shardId, uint mainAccountId, uint linkedAccountId, Guid idempotencyKey, CancellationToken cancellationToken = default)
    {
        UnlinkCalls.Add((mainAccountId, linkedAccountId));
        return Task.FromResult(NextUnlinkOutcome);
    }

    public Task<IReadOnlyCollection<uint>> GetOwnershipGroupAccountIdsAsync(
        string shardId, uint accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyCollection<uint>>(
            _groupAccountIdsByMainAccountId.TryGetValue(accountId, out var ids) ? ids.ToArray() : [accountId]);

    public Task<Guid?> TryGetOwnershipGroupIdAsync(string shardId, uint mainAccountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_groupIdByMainAccountId.TryGetValue(mainAccountId, out var groupId) ? groupId : (Guid?)null);
}
