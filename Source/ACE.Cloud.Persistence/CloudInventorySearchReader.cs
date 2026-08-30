using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Persistence;

/// <summary>
/// The authorization-scoped, typed inventory search (issue #32 Green: "Implement prepared indexed
/// candidate search and typed query parsing ... without string-built SQL"). Reuses
/// <see cref="CloudInventoryCandidateReader"/>'s exact authorization-scoped candidate fetch
/// (<see cref="CloudInventoryQueryReader"/> shares it too -- see that type's doc comment), so search
/// never fetches an unauthorized row before filtering it out in memory, and delegates every
/// text/property/Safe Regex Search decision to the pure <see cref="CloudInventorySearchEngine"/>.
/// Safe Regex Search's admin disablement flag is read fresh from
/// <see cref="CloudSearchConfigurationBoundary"/> on every call (ADM-001's "revalidate on every
/// sensitive request" discipline applied to a feature flag, not just admin identity), so a
/// mid-session disablement takes effect on the very next search without needing this reader to be
/// restarted or invalidate a cache. <paramref name="rateLimitResult"/> mirrors
/// <c>AuthSessionEndpoints.HandleLoginAsync</c>'s existing shape (the caller registers the attempt
/// against a <see cref="CloudLoginAttemptRateLimiter"/> keyed by session/IP and passes the outcome
/// in) rather than this reader owning a limiter itself, since which key identifies "the caller" is an
/// HTTP-boundary concern this storage-layer type has no business deciding.
/// </summary>
public sealed class CloudInventorySearchReader
{
    private readonly CloudDbContext _context;

    public CloudInventorySearchReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CloudInventorySearchResponse> SearchAsync(
        string shardId,
        CloudLiveStreamViewer viewer,
        CloudInventorySearchRequest request,
        CloudRateLimitResult rateLimitResult,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An inventory search requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(rateLimitResult);

        if (request.Page <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A Mule Page number must be positive.");
        }

        var candidateReader = new CloudInventoryCandidateReader(_context);
        var candidates = await candidateReader.GetAuthorizedCandidatesAsync(shardId, viewer, cancellationToken);

        var configuration = await new CloudSearchConfigurationBoundary(_context).GetCurrentAsync(shardId, cancellationToken);

        var filter = new CloudInventorySearchFilter
        {
            Category = request.Category,
            NameContains = request.NameContains,
            MinValue = request.MinValue,
            MaxValue = request.MaxValue,
            MinBurden = request.MinBurden,
            MaxBurden = request.MaxBurden,
            MinQuantity = request.MinQuantity,
            MaxQuantity = request.MaxQuantity,
            RegexPattern = request.RegexPattern,
            Page = request.Page,
            SortKey = request.SortKey,
            SortDirection = request.SortDirection,
        };

        var result = CloudInventorySearchEngine.Search(candidates, filter, configuration.RegexSearchEnabled, rateLimitResult, cancellationToken);
        var asOfSequenceNumber = await candidateReader.GetCustodyProjectionCheckpointAsync(shardId, cancellationToken);

        return new CloudInventorySearchResponse(result.Kind, result.Page, result.Reason, asOfSequenceNumber);
    }
}
