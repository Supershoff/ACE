using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The one narrow, structurally safe seam through which a public (non-admin) HTTP route may ever
/// read from <see cref="IProtectedAssetBlobStore"/> (issue #31, ASSET-004: "Generated public
/// derivatives must not expose the source DAT through path traversal, arbitrary range access, or raw
/// download endpoints"). Every other consumer of <see cref="IProtectedAssetBlobStore"/> handles
/// upload sessions, retained source DATs, or staged manifest entries and stays admin-only; this type
/// can only ever resolve the fixed <c>icon-cache/&lt;hex&gt;.png</c> namespace
/// (<see cref="CloudAssetStagingPathPolicy.BuildIconCompositionCacheRelativePath"/>) because its sole
/// parameter is a <see cref="CloudIconCompositionCacheKey"/>, never a caller-supplied path -- there is
/// no way to reach the source DAT, a manifest, or another shard's data through this method's
/// signature.
/// </summary>
public sealed class CloudIconDerivativeReader : ICloudIconDerivativeReader
{
    private readonly IProtectedAssetBlobStore _blobStore;

    public CloudIconDerivativeReader(IProtectedAssetBlobStore blobStore)
    {
        _blobStore = blobStore ?? throw new ArgumentNullException(nameof(blobStore));
    }

    public async Task<byte[]?> TryReadAsync(CloudIconCompositionCacheKey cacheKey, CancellationToken cancellationToken = default)
    {
        var relativePath = CloudAssetStagingPathPolicy.BuildIconCompositionCacheRelativePath(cacheKey);

        if (!await _blobStore.ExistsAsync(relativePath, cancellationToken))
        {
            return null;
        }

        await using var stream = await _blobStore.OpenReadAsync(relativePath, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
