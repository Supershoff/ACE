namespace ACE.Cloud.Domain;

/// <summary>
/// The lifecycle of one versioned Asset Manifest (ASSET-002, ASSET-004). A manifest is created only
/// once its source session's staging/extraction has produced a complete set of entries; it never
/// exists in a partially-extracted state visible to <see cref="CloudAssetManifestActivationPolicy"/>.
/// </summary>
public enum CloudAssetManifestState
{
    /// <summary>Extraction into staging succeeded and produced a complete manifest, but it is not yet the active one.</summary>
    StagingComplete,

    /// <summary>The currently active manifest for its shard/kind (ASSET-002: "atomically replaces the active asset manifest").</summary>
    Active,

    /// <summary>A previously active manifest, kept for audit/history after a newer one activated.</summary>
    Superseded,
}
