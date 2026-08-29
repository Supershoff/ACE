using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

public sealed record CloudAssetManifestEntrySnapshot(uint Did, CloudAssetFileKind FileKind, string RelativePath, long ByteLength, string Sha256Hex);

/// <summary>A read-only projection of one <see cref="CloudAssetManifest"/>, optionally with its entries.</summary>
public sealed record CloudAssetManifestSnapshot(
    Guid Id,
    string ShardId,
    CloudAssetKind Kind,
    int Version,
    CloudAssetManifestState State,
    Guid SourceImportSessionId,
    int EntryCount,
    DateTime CreatedAtUtc,
    DateTime? ActivatedAtUtc,
    IReadOnlyList<CloudAssetManifestEntrySnapshot> Entries)
{
    public static CloudAssetManifestSnapshot From(CloudAssetManifest manifest, IReadOnlyList<CloudAssetManifestEntrySnapshot>? entries = null) => new(
        manifest.Id, manifest.ShardId, manifest.Kind, manifest.Version, manifest.State, manifest.SourceImportSessionId,
        manifest.EntryCount, manifest.CreatedAtUtc, manifest.ActivatedAtUtc, entries ?? []);
}

public sealed record CloudAssetManifestEntryInput(CloudAssetManifestEntryKey Key, string RelativePath, long ByteLength, string Sha256Hex);
