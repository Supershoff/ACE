using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>A deterministic in-memory <see cref="ICloudIconLayerSource"/> double for Red/Green tests.</summary>
internal sealed class FakeCloudIconLayerSource : ICloudIconLayerSource
{
    private readonly Dictionary<CloudIconLayerReference, CloudIconLayerResolution> _resolutions = new();
    public int ResolveCallCount { get; private set; }

    public FakeCloudIconLayerSource WithResolved(CloudIconLayerKind kind, uint did, CloudIconRasterLayer raster)
    {
        _resolutions[new CloudIconLayerReference(kind, did)] = CloudIconLayerResolution.Resolved(raster);
        return this;
    }

    public FakeCloudIconLayerSource WithFailure(CloudIconLayerKind kind, uint did, CloudIconLayerResolutionOutcomeKind reason)
    {
        _resolutions[new CloudIconLayerReference(kind, did)] = CloudIconLayerResolution.Failed(reason);
        return this;
    }

    public Task<CloudIconLayerResolution> ResolveAsync(
        CloudIconLayerReference reference, IReadOnlyList<CloudIconPaletteRangeOverride> paletteOverrides, CancellationToken cancellationToken = default)
    {
        ResolveCallCount++;

        if (!_resolutions.TryGetValue(reference, out var resolution))
        {
            throw new InvalidOperationException($"No fake resolution configured for {reference.Kind}:{reference.Did:x8}.");
        }

        return Task.FromResult(resolution);
    }

    public static CloudIconRasterLayer SolidRaster(int width, int height, byte r, byte g, byte b, byte a)
    {
        var rgba = new byte[width * height * 4];
        for (var pixel = 0; pixel < width * height; pixel++)
        {
            rgba[(pixel * 4) + 0] = r;
            rgba[(pixel * 4) + 1] = g;
            rgba[(pixel * 4) + 2] = b;
            rgba[(pixel * 4) + 3] = a;
        }

        return new CloudIconRasterLayer(width, height, rgba);
    }
}
