namespace ACE.Cloud.Domain;

/// <summary>
/// Whether a staged Asset Manifest may become the active one for its shard/kind (ASSET-002:
/// "Activate a completed manifest with one database transaction/pointer swap"; Red test: "activation
/// race"). Only a complete (<see cref="CloudAssetManifestState.StagingComplete"/>), non-empty
/// manifest may activate, and only if no strictly newer manifest has already won the race -- two
/// concurrent activation attempts for manifests built from the same or an older session must never
/// let the older one clobber a newer already-committed activation.
/// </summary>
public static class CloudAssetManifestActivationPolicy
{
    public static CloudAssetManifestActivationDecision Evaluate(
        CloudAssetManifestState manifestState,
        int manifestVersion,
        int entryCount,
        int? currentActiveVersion)
    {
        if (manifestState != CloudAssetManifestState.StagingComplete)
        {
            return CloudAssetManifestActivationDecision.Rejected(
                $"Manifest version {manifestVersion} is in state {manifestState} and cannot be activated. Only a complete staged manifest may activate.");
        }

        if (entryCount <= 0)
        {
            return CloudAssetManifestActivationDecision.Rejected(
                $"Manifest version {manifestVersion} has no entries; an empty manifest may never become active.");
        }

        if (currentActiveVersion is { } activeVersion && manifestVersion <= activeVersion)
        {
            return CloudAssetManifestActivationDecision.Rejected(
                $"Manifest version {manifestVersion} is not newer than the currently active version {activeVersion}.");
        }

        return CloudAssetManifestActivationDecision.Approved();
    }
}
