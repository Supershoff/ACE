using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>A deterministic in-memory <see cref="ICloudIconClothingEffectResolver"/> double for Red/Green tests.</summary>
internal sealed class FakeCloudIconClothingEffectResolver : ICloudIconClothingEffectResolver
{
    private readonly Dictionary<(uint ClothingBaseDid, uint SetupTableId), CloudIconClothingResolution> _effects = new();

    public FakeCloudIconClothingEffectResolver With(uint clothingBaseDid, uint setupTableId, CloudIconClothingResolution resolution)
    {
        _effects[(clothingBaseDid, setupTableId)] = resolution;
        return this;
    }

    public Task<CloudIconClothingResolution?> ResolveAsync(
        uint clothingBaseDid, uint setupTableId, int? paletteTemplate, float? shade, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_effects.TryGetValue((clothingBaseDid, setupTableId), out var resolution) ? resolution : null);
    }
}
