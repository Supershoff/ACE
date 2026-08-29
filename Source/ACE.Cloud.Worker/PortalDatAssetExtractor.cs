using System.Security.Cryptography;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.DatLoader;

namespace ACE.Cloud.Worker;

/// <summary>
/// The production <see cref="IPortalDatAssetExtractor"/>: opens the retained source DAT directly
/// with <c>ACE.DatLoader</c> (the same reader ACE's own <c>image-export</c> console command uses)
/// and extracts every raw entry's undecoded bytes for the file kinds Icon Reconstruction needs --
/// exactly the DID-addressable inputs ASSET-004 asks for. <see cref="ACE.DatLoader.DatFileType.Texture"/>
/// covers base/underlay/overlay icon layers; <see cref="ACE.DatLoader.DatFileType.Palette"/>,
/// <see cref="ACE.DatLoader.DatFileType.PaletteSet"/>, and <see cref="ACE.DatLoader.DatFileType.Clothing"/>
/// (added by issue #26) cover the ClothingBase/PaletteTemplate/Shade resolution chain UI-005 requires.
/// Decoding these bytes into displayable pixels is deliberately out of scope here: that is
/// <c>CloudIconCompositor</c>'s job. Never touches ACE's process-wide <see cref="DatManager"/>
/// singleton: this opens its own short-lived <see cref="PortalDatDatabase"/> instance so extracting
/// one shard's DAT can never contend with or be confused for the live ACE world process's own DAT
/// access.
/// </summary>
public sealed class PortalDatAssetExtractor : IPortalDatAssetExtractor
{
    private static readonly IReadOnlyDictionary<DatFileType, CloudAssetFileKind> ExtractedFileKinds = new Dictionary<DatFileType, CloudAssetFileKind>
    {
        [DatFileType.Texture] = CloudAssetFileKind.Texture,
        [DatFileType.Palette] = CloudAssetFileKind.Palette,
        [DatFileType.PaletteSet] = CloudAssetFileKind.PaletteSet,
        [DatFileType.Clothing] = CloudAssetFileKind.Clothing,
    };

    public Task<IReadOnlyList<CloudAssetManifestEntryInput>> ExtractAsync(
        string sourceDatAbsolutePath, Guid manifestId, IProtectedAssetBlobStore blobStore, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDatAbsolutePath))
        {
            throw new ArgumentException("A source DAT path is required.", nameof(sourceDatAbsolutePath));
        }

        ArgumentNullException.ThrowIfNull(blobStore);

        return ExtractCoreAsync(sourceDatAbsolutePath, manifestId, blobStore, cancellationToken);
    }

    private static async Task<IReadOnlyList<CloudAssetManifestEntryInput>> ExtractCoreAsync(
        string sourceDatAbsolutePath, Guid manifestId, IProtectedAssetBlobStore blobStore, CancellationToken cancellationToken)
    {
        var database = new PortalDatDatabase(sourceDatAbsolutePath, keepOpen: false);

        var entries = new List<CloudAssetManifestEntryInput>();

        foreach (var (did, datFile) in database.AllFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var datFileType = datFile.GetFileType(DatDatabaseType.Portal);
            if (did == 0 || datFileType is null || !ExtractedFileKinds.TryGetValue(datFileType.Value, out var fileKind))
            {
                continue;
            }

            var reader = database.GetReaderForFile(did);
            if (reader is null)
            {
                continue;
            }

            var bytes = reader.Buffer;
            var key = new CloudAssetManifestEntryKey(did, fileKind);
            var relativePath = CloudAssetStagingPathPolicy.BuildManifestEntryRelativePath(manifestId, key);

            await blobStore.WriteAsync(relativePath, bytes, cancellationToken);

            entries.Add(new CloudAssetManifestEntryInput(key, relativePath, bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))));
        }

        return entries;
    }
}
