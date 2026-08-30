using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudDisplayCharacterGateway"/> substitute.</summary>
internal sealed class FakeCloudDisplayCharacterGateway : ICloudDisplayCharacterGateway
{
    private readonly Dictionary<Guid, CloudDisplayCharacterSelection> _selectionsByGroupId = [];

    public List<(Guid GroupId, IReadOnlyList<CloudDisplayCharacterCandidate> Candidates, CloudDisplayCharacterSelectionReason Reason)> ReselectCalls { get; } = [];

    public void Seed(Guid groupId, string shardId, uint characterId, string characterName, int totalLogins)
    {
        var result = CloudDisplayCharacterSelectionResult.Selected(new CloudDisplayCharacterCandidate(characterId, characterName, totalLogins));
        _selectionsByGroupId[groupId] = CloudDisplayCharacterSelection.Create(groupId, shardId, result, DateTime.UtcNow);
    }

    public Task<CloudDisplayCharacterSelectionResult> ReselectAsync(
        string shardId,
        Guid ownershipGroupId,
        IReadOnlyList<CloudDisplayCharacterCandidate> candidates,
        CloudDisplayCharacterSelectionReason reason,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        ReselectCalls.Add((ownershipGroupId, candidates, reason));

        var result = CloudDisplayCharacterSelectionPolicy.SelectDefault(candidates);
        _selectionsByGroupId[ownershipGroupId] = CloudDisplayCharacterSelection.Create(ownershipGroupId, shardId, result, DateTime.UtcNow);
        return Task.FromResult(result);
    }

    public Task<CloudDisplayCharacterSelection?> GetCurrentSelectionAsync(Guid ownershipGroupId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_selectionsByGroupId.GetValueOrDefault(ownershipGroupId));
}
