using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudDisplayCharacterReader"/> substitute for endpoint tests.</summary>
internal sealed class FakeCloudDisplayCharacterReader : ICloudDisplayCharacterReader
{
    private readonly Dictionary<Guid, CloudDisplayCharacterSelection> _selectionsByGroupId = [];

    public void Seed(Guid ownershipGroupId, CloudDisplayCharacterSelection selection) => _selectionsByGroupId[ownershipGroupId] = selection;

    public Task<CloudDisplayCharacterSelection?> GetCurrentSelectionAsync(Guid ownershipGroupId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_selectionsByGroupId.TryGetValue(ownershipGroupId, out var selection) ? selection : null);
}
