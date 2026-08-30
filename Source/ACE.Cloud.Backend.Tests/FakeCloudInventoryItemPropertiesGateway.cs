using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudInventoryItemPropertiesGateway"/> substitute for Backend endpoint tests.</summary>
internal sealed class FakeCloudInventoryItemPropertiesGateway : ICloudInventoryItemPropertiesGateway
{
    private readonly Dictionary<uint, CloudInventoryItemPropertiesProjection> _rowsByBiotaId = [];

    public void Seed(uint biotaId, string shardId, string name, int? value, int? burden) =>
        _rowsByBiotaId[biotaId] = CloudInventoryItemPropertiesProjection.TryApply(
            current: null,
            biotaId,
            shardId,
            name,
            ACE.Entity.Enum.ItemType.None,
            ACE.Entity.Enum.WeenieType.Generic,
            value,
            burden,
            iconCacheKeyHex: null,
            revision: 1).Row;

    public Task<CloudInventoryItemPropertiesProjection?> TryGetAsync(uint biotaId, string shardId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_rowsByBiotaId.TryGetValue(biotaId, out var row) ? row : null);
}
