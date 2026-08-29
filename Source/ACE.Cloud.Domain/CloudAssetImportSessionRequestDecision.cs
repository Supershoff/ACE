namespace ACE.Cloud.Domain;

public sealed record CloudAssetImportSessionRequestDecision(CloudAssetImportSessionRequestOutcomeKind Kind, string? Reason)
{
    public bool IsValid => Kind == CloudAssetImportSessionRequestOutcomeKind.Valid;

    public static CloudAssetImportSessionRequestDecision Valid() => new(CloudAssetImportSessionRequestOutcomeKind.Valid, null);

    public static CloudAssetImportSessionRequestDecision Invalid(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("An invalid session request decision requires a reason.", nameof(reason));
        }

        return new CloudAssetImportSessionRequestDecision(CloudAssetImportSessionRequestOutcomeKind.Invalid, reason);
    }
}
