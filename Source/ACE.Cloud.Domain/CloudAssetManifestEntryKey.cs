namespace ACE.Cloud.Domain;

/// <summary>
/// The DID-addressable identity of one manifest entry (ASSET-004: "Build a DID-addressable
/// manifest"). This type has no constructor that accepts a free-form string: it is built only from
/// a validated numeric DAT file ID and a closed <see cref="CloudAssetFileKind"/> enum, so nothing
/// derived from it (see <see cref="CloudAssetStagingPathPolicy"/>) can ever carry a path-traversal
/// sequence, a null byte, or any other attacker-influenced path segment.
/// </summary>
public readonly record struct CloudAssetManifestEntryKey
{
    public uint Did { get; }

    public CloudAssetFileKind Kind { get; }

    public CloudAssetManifestEntryKey(uint did, CloudAssetFileKind kind)
    {
        if (did == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(did), "A manifest entry requires a real, non-zero DAT file ID.");
        }

        Did = did;
        Kind = kind;
    }

    /// <summary>An 8-digit lowercase hex rendering of <see cref="Did"/>, safe to use as a path segment or file name.</summary>
    public string DidHex => Did.ToString("x8");

    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()}/{DidHex}";
}
