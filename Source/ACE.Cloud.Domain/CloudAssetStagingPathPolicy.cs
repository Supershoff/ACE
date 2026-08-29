using System.Text.RegularExpressions;

namespace ACE.Cloud.Domain;

/// <summary>
/// Builds every relative path Asset Import protected storage ever writes to or reads from
/// (ASSET-004: "must not expose the source DAT through path traversal, arbitrary range access, or
/// raw download endpoints"). Every method takes only structured, already-validated identifiers
/// (GUIDs, non-negative integers, a closed enum, and a shard ID validated once by
/// <see cref="RequireSafeShardId"/>) -- never a caller-supplied free-form string -- so the resulting
/// path can never contain "..", an absolute-path escape, or a null byte. Callers must still join the
/// returned relative path under a fixed storage root rather than trusting it as an absolute path.
/// </summary>
public static class CloudAssetStagingPathPolicy
{
    private static readonly Regex SafeShardIdPattern = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);

    public static string BuildChunkPartRelativePath(Guid sessionId, int chunkIndex)
    {
        RequireRealGuid(sessionId, nameof(sessionId));
        if (chunkIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        return $"sessions/{sessionId:N}/chunk-{chunkIndex:D8}.part";
    }

    public static string BuildAssembledUploadRelativePath(Guid sessionId)
    {
        RequireRealGuid(sessionId, nameof(sessionId));

        return $"sessions/{sessionId:N}/assembled.dat";
    }

    public static string BuildRetainedSourceRelativePath(string shardId, CloudAssetKind kind)
    {
        RequireSafeShardId(shardId);

        return $"retained/{shardId}/{kind.ToString().ToLowerInvariant()}.dat";
    }

    public static string BuildManifestEntryRelativePath(Guid manifestId, CloudAssetManifestEntryKey key)
    {
        RequireRealGuid(manifestId, nameof(manifestId));

        return $"manifests/{manifestId:N}/{key.Kind.ToString().ToLowerInvariant()}/{key.DidHex}.bin";
    }

    /// <summary>
    /// Validates a shard ID is safe to use as a directory segment. Cloud Mule shard IDs are
    /// operator configuration, not request input (ARCH-001), but this still enforces the same
    /// structural guarantee as every other path builder here rather than trusting configuration
    /// blindly.
    /// </summary>
    public static void RequireSafeShardId(string shardId)
    {
        if (string.IsNullOrWhiteSpace(shardId) || !SafeShardIdPattern.IsMatch(shardId))
        {
            throw new ArgumentException(
                $"\"{shardId}\" is not a safe Cloud Shard ID for storage paths. Expected 1-64 characters of letters, digits, '_' or '-'.",
                nameof(shardId));
        }
    }

    private static void RequireRealGuid(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A real (non-empty) identifier is required.", paramName);
        }
    }
}
