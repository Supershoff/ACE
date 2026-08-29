namespace ACE.Cloud.Domain;

/// <summary>
/// Whether a new Asset Import may begin for one shard/<see cref="CloudAssetKind"/>, given the state
/// of the most recent existing session for that same key (ASSET-002's Red test: "concurrent
/// imports"). Only <see cref="CloudAssetImportSessionState.Uploading"/> and
/// <see cref="CloudAssetImportSessionState.Staging"/> are in-flight; every other state is terminal
/// and frees the key for a brand-new session.
/// </summary>
public static class CloudAssetImportConcurrencyPolicy
{
    public static bool IsInFlight(CloudAssetImportSessionState state) =>
        state is CloudAssetImportSessionState.Uploading or CloudAssetImportSessionState.Staging;

    /// <summary>
    /// True when a caller may create a brand-new session for this shard/kind. False means the
    /// caller must instead resume the existing in-flight session (<paramref name="existingState"/>
    /// is <see cref="CloudAssetImportSessionState.Uploading"/>) or wait for staging to finish
    /// (<paramref name="existingState"/> is <see cref="CloudAssetImportSessionState.Staging"/>).
    /// </summary>
    public static bool CanStartNewImport(CloudAssetImportSessionState? existingState) =>
        existingState is null || !IsInFlight(existingState.Value);

    /// <summary>
    /// True when a caller may resume uploading chunks against the existing session rather than
    /// starting a new one (ASSET-002: "interrupted/resumed upload").
    /// </summary>
    public static bool CanResume(CloudAssetImportSessionState existingState) =>
        existingState == CloudAssetImportSessionState.Uploading;
}
