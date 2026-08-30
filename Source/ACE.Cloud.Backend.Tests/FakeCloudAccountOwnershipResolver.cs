using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudAccountOwnershipResolver"/> substitute, defaulting every account to standalone (its own effective owner) unless a test configures a link.</summary>
internal sealed class FakeCloudAccountOwnershipResolver : ICloudAccountOwnershipResolver
{
    private readonly Dictionary<uint, uint> _effectiveMainAccountIdByAccountId = [];

    public void SetLinked(uint accountId, uint mainAccountId) => _effectiveMainAccountIdByAccountId[accountId] = mainAccountId;

    public Task<uint> ResolveEffectiveOwnerAccountIdAsync(string shardId, uint accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_effectiveMainAccountIdByAccountId.GetValueOrDefault(accountId, accountId));
}
