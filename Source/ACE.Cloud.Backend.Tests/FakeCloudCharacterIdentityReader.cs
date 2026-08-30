using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudCharacterIdentityReader"/> substitute.</summary>
internal sealed class FakeCloudCharacterIdentityReader : ICloudCharacterIdentityReader
{
    private readonly Dictionary<uint, CloudDisplayCharacterCandidate> _candidatesByAccountId = [];

    public void Seed(uint accountId, CloudDisplayCharacterCandidate candidate) => _candidatesByAccountId[accountId] = candidate;

    public Task<IReadOnlyList<CloudDisplayCharacterCandidate>> GetCandidatesAsync(
        string shardId, IReadOnlyCollection<uint> accountIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudDisplayCharacterCandidate>>(
            accountIds.Where(_candidatesByAccountId.ContainsKey).Select(id => _candidatesByAccountId[id]).ToArray());
}
