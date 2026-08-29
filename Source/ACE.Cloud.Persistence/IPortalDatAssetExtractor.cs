namespace ACE.Cloud.Persistence;

/// <summary>
/// Extracts every approved, DID-addressable asset from a client DAT file into protected staging
/// (ASSET-002's "background staging/extraction jobs", ASSET-004's "DID-addressable manifest"). The
/// production implementation (<c>ACE.Cloud.Worker.PortalDatAssetExtractor</c>) uses
/// <c>ACE.DatLoader</c> against the shard's retained source DAT; this seam exists so
/// <see cref="CloudAssetImportBoundary"/>'s staging plumbing can be exercised with a synthetic
/// double, keeping CI coverage independent of a real, non-committable client DAT (Refactor:
/// "keep synthetic CI coverage distinct from protected golden verification").
/// </summary>
public interface IPortalDatAssetExtractor
{
    /// <summary>
    /// Reads <paramref name="sourceDatAbsolutePath"/> and writes every extracted entry's bytes into
    /// <paramref name="blobStore"/> under <paramref name="manifestId"/>, returning the manifest
    /// entries produced. Throws on a malformed/unreadable source file; the caller is responsible for
    /// turning that into a <see cref="CloudAssetImportBoundary.FailStagingAsync"/> call.
    /// </summary>
    Task<IReadOnlyList<CloudAssetManifestEntryInput>> ExtractAsync(
        string sourceDatAbsolutePath, Guid manifestId, IProtectedAssetBlobStore blobStore, CancellationToken cancellationToken = default);
}
