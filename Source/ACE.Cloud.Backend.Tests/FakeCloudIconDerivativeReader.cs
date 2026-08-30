using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudIconDerivativeReader"/> substitute for Backend endpoint tests.</summary>
internal sealed class FakeCloudIconDerivativeReader : ICloudIconDerivativeReader
{
    private readonly Dictionary<string, byte[]> _pngBytesByHex = [];

    public void Seed(string hex, byte[] pngBytes) => _pngBytesByHex[hex] = pngBytes;

    public Task<byte[]?> TryReadAsync(CloudIconCompositionCacheKey cacheKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pngBytesByHex.TryGetValue(cacheKey.Hex, out var bytes) ? bytes : null);
}
