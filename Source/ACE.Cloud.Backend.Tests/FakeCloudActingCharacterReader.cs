using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudActingCharacterReader"/> substitute for the Allegiance Vault Acting Character selector.</summary>
internal sealed class FakeCloudActingCharacterReader : ICloudActingCharacterReader
{
    private readonly Dictionary<uint, List<CloudActingCharacterSummary>> _charactersByAccountId = [];

    public void SetCharacters(uint accountId, params CloudActingCharacterSummary[] characters) =>
        _charactersByAccountId[accountId] = characters.ToList();

    public Task<IReadOnlyList<CloudActingCharacterSummary>> GetCurrentCharactersAsync(
        string shardId, uint accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudActingCharacterSummary>>(
            _charactersByAccountId.TryGetValue(accountId, out var characters) ? characters : []);
}
