namespace ACE.Cloud.Domain;

public sealed record CloudAssetUploadFinalizationDecision(CloudAssetUploadFinalizationOutcomeKind Kind, string? Reason)
{
    public bool IsCompleted => Kind == CloudAssetUploadFinalizationOutcomeKind.Completed;

    public static CloudAssetUploadFinalizationDecision Completed() =>
        new(CloudAssetUploadFinalizationOutcomeKind.Completed, null);

    public static CloudAssetUploadFinalizationDecision Incomplete(string reason) =>
        new(CloudAssetUploadFinalizationOutcomeKind.Incomplete, reason);

    public static CloudAssetUploadFinalizationDecision ChecksumMismatch(string reason) =>
        new(CloudAssetUploadFinalizationOutcomeKind.ChecksumMismatch, reason);

    public static CloudAssetUploadFinalizationDecision InvalidState(string reason) =>
        new(CloudAssetUploadFinalizationOutcomeKind.InvalidState, reason);
}
