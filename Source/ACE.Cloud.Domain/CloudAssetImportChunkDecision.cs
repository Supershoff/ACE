namespace ACE.Cloud.Domain;

public sealed record CloudAssetImportChunkDecision(CloudAssetImportChunkOutcomeKind Kind, string? RejectionReason)
{
    public static CloudAssetImportChunkDecision Accepted() => new(CloudAssetImportChunkOutcomeKind.Accepted, null);

    public static CloudAssetImportChunkDecision DuplicateIgnored() => new(CloudAssetImportChunkOutcomeKind.DuplicateIgnored, null);

    public static CloudAssetImportChunkDecision Rejected(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected chunk decision requires a reason.", nameof(reason));
        }

        return new CloudAssetImportChunkDecision(CloudAssetImportChunkOutcomeKind.Rejected, reason);
    }
}
