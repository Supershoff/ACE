using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Worker;

/// <summary>
/// Reads one active manifest's extracted raw bytes by DID/kind through protected blob storage
/// (ASSET-004). This is the single seam every Icon Reconstruction resolver uses instead of touching
/// ACE's process-wide <c>DatManager</c> singleton or the retained source DAT directly -- everything
/// downstream of this type sees only whatever one specific manifest version already extracted.
/// </summary>
public sealed class CloudAssetManifestBlobReader
{
    private readonly IReadOnlyDictionary<CloudAssetManifestEntryKey, string> _relativePathsByKey;
    private readonly IProtectedAssetBlobStore _blobStore;

    public CloudAssetManifestBlobReader(
        IReadOnlyDictionary<CloudAssetManifestEntryKey, string> relativePathsByKey, IProtectedAssetBlobStore blobStore)
    {
        ArgumentNullException.ThrowIfNull(relativePathsByKey);
        ArgumentNullException.ThrowIfNull(blobStore);

        _relativePathsByKey = relativePathsByKey;
        _blobStore = blobStore;
    }

    public static CloudAssetManifestBlobReader FromEntries(
        IEnumerable<CloudAssetManifestEntryRecord> entries, IProtectedAssetBlobStore blobStore)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return new CloudAssetManifestBlobReader(
            entries.ToDictionary(entry => entry.ToKey(), entry => entry.RelativePath), blobStore);
    }

    /// <summary>Returns null when no manifest entry exists for <paramref name="did"/>/<paramref name="kind"/>.</summary>
    public async Task<byte[]?> TryReadAsync(uint did, CloudAssetFileKind kind, CancellationToken cancellationToken = default)
    {
        if (!_relativePathsByKey.TryGetValue(new CloudAssetManifestEntryKey(did, kind), out var relativePath))
        {
            return null;
        }

        await using var stream = await _blobStore.OpenReadAsync(relativePath, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
