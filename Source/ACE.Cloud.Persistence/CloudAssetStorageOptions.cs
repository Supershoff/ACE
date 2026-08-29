namespace ACE.Cloud.Persistence;

/// <summary>
/// Operator configuration for Asset Import's protected storage (ASSET-002's "bounded protected
/// storage"). None of this is committed deployment data (ARCH-013): <see cref="RootDirectory"/>
/// always points outside the repository.
/// </summary>
public sealed class CloudAssetStorageOptions
{
    public const string SectionName = "CloudAssetStorage";

    /// <summary>The absolute path under which every session/manifest/retained-source file is written. Never served directly over HTTP.</summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>The largest total declared upload size a new Asset Import session may request.</summary>
    public long MaxTotalBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>The largest single chunk a session's declared plan may specify.</summary>
    public int MaxChunkSizeBytes { get; set; } = 32 * 1024 * 1024;
}
