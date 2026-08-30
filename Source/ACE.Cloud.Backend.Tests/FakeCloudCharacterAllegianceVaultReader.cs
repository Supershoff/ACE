using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudCharacterAllegianceVaultReader"/> substitute.</summary>
internal sealed class FakeCloudCharacterAllegianceVaultReader : ICloudCharacterAllegianceVaultReader
{
    private readonly Dictionary<uint, List<Guid>> _vaultOwnerIdsByAccountId = [];

    public void SetVaultOwnerIds(uint accountId, params Guid[] vaultOwnerIds) => _vaultOwnerIdsByAccountId[accountId] = [.. vaultOwnerIds];

    public Task<IReadOnlyList<Guid>> GetCurrentAllegianceVaultOwnerIdsAsync(
        string shardId, uint accountId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Guid> result = _vaultOwnerIdsByAccountId.GetValueOrDefault(accountId, []);
        return Task.FromResult(result);
    }
}
