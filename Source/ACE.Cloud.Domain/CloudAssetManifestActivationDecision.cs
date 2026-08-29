namespace ACE.Cloud.Domain;

public sealed record CloudAssetManifestActivationDecision(CloudAssetManifestActivationOutcomeKind Kind, string? RejectionReason)
{
    public bool IsApproved => Kind == CloudAssetManifestActivationOutcomeKind.Approved;

    public static CloudAssetManifestActivationDecision Approved() => new(CloudAssetManifestActivationOutcomeKind.Approved, null);

    public static CloudAssetManifestActivationDecision Rejected(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected activation decision requires a reason.", nameof(reason));
        }

        return new CloudAssetManifestActivationDecision(CloudAssetManifestActivationOutcomeKind.Rejected, reason);
    }
}
