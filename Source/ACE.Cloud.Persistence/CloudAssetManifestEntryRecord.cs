using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// One DID-addressable entry of a <see cref="CloudAssetManifest"/> (ASSET-004). Immutable once
/// inserted; a manifest's whole entry set is written once, in the same transaction as the manifest
/// row itself (<see cref="CloudAssetImportBoundary.CompleteStagingAsync"/>).
/// </summary>
public sealed class CloudAssetManifestEntryRecord
{
    private CloudAssetManifestEntryRecord()
    {
    }

    public CloudAssetManifestEntryRecord(Guid manifestId, uint did, CloudAssetFileKind fileKind, string relativePath, long byteLength, string sha256Hex)
    {
        if (manifestId == Guid.Empty)
        {
            throw new ArgumentException("A manifest entry requires its manifest ID.", nameof(manifestId));
        }

        if (did == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(did), "A manifest entry requires a real, non-zero DAT file ID.");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("A manifest entry requires its staged relative path.", nameof(relativePath));
        }

        if (byteLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength));
        }

        if (string.IsNullOrWhiteSpace(sha256Hex))
        {
            throw new ArgumentException("A manifest entry requires its checksum.", nameof(sha256Hex));
        }

        ManifestId = manifestId;
        Did = did;
        FileKind = fileKind;
        RelativePath = relativePath;
        ByteLength = byteLength;
        Sha256Hex = sha256Hex;
    }

    public Guid ManifestId { get; private set; }

    public uint Did { get; private set; }

    public CloudAssetFileKind FileKind { get; private set; }

    /// <summary>A path built exclusively by <see cref="CloudAssetStagingPathPolicy"/>, relative to protected storage's root.</summary>
    public string RelativePath { get; private set; } = null!;

    public long ByteLength { get; private set; }

    public string Sha256Hex { get; private set; } = null!;

    public CloudAssetManifestEntryKey ToKey() => new(Did, FileKind);
}
