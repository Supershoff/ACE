namespace ACE.Cloud.Persistence;

/// <summary>The kind of audited fact one <see cref="CloudAssetImportLedgerEvent"/> records (EVT-001, EVT-002).</summary>
public enum CloudAssetImportLedgerEventType
{
    Started,
    ChecksumFailed,
    StagingCompleted,
    StagingFailed,
    ManifestActivated,
    ManifestActivationRejected,
    ReprocessRequested,
}
