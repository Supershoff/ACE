namespace ACE.Cloud.Domain;

/// <summary>
/// The lifecycle of one resumable Asset Import session (ASSET-002). <see cref="Uploading"/> is the
/// only state a chunk may still be applied to and the only state a session may resume into after an
/// interruption. Every other state is terminal for the session itself: <see cref="ChecksumFailed"/>
/// and <see cref="StagingFailed"/> record a failed attempt without ever touching the active asset
/// manifest ("failed import cannot disturb active assets"); <see cref="StagingComplete"/> hands off
/// to the separate, explicit <see cref="CloudAssetManifestState"/> activation step;
/// <see cref="Cancelled"/> records an administrator-abandoned upload.
/// </summary>
public enum CloudAssetImportSessionState
{
    Uploading,
    ChecksumFailed,
    Staging,
    StagingFailed,
    StagingComplete,
    Cancelled,
}
