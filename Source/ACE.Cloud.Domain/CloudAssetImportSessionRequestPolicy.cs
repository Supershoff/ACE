namespace ACE.Cloud.Domain;

/// <summary>
/// Bounds a new Asset Import's declared plan against operator-configured limits before any storage
/// is reserved (ASSET-002's "bounded protected storage"; review rule against unbounded work on
/// public inputs). An admin request that declares an implausibly large upload or chunk size is
/// rejected up front instead of being allowed to reserve disk space or memory proportional to an
/// attacker-chosen number.
/// </summary>
public static class CloudAssetImportSessionRequestPolicy
{
    public static CloudAssetImportSessionRequestDecision Evaluate(
        long totalBytes, int chunkSizeBytes, long maxTotalBytes, int maxChunkSizeBytes)
    {
        if (totalBytes <= 0)
        {
            return CloudAssetImportSessionRequestDecision.Invalid("The declared upload size must be positive.");
        }

        if (chunkSizeBytes <= 0)
        {
            return CloudAssetImportSessionRequestDecision.Invalid("The declared chunk size must be positive.");
        }

        if (totalBytes > maxTotalBytes)
        {
            return CloudAssetImportSessionRequestDecision.Invalid(
                $"The declared upload size {totalBytes} exceeds the configured maximum of {maxTotalBytes} bytes.");
        }

        if (chunkSizeBytes > maxChunkSizeBytes)
        {
            return CloudAssetImportSessionRequestDecision.Invalid(
                $"The declared chunk size {chunkSizeBytes} exceeds the configured maximum of {maxChunkSizeBytes} bytes.");
        }

        return CloudAssetImportSessionRequestDecision.Valid();
    }
}
