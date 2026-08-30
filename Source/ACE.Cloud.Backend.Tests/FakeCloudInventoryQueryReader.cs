using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// An in-memory <see cref="ICloudInventoryQueryReader"/> substitute that still runs the real, pure
/// <see cref="CloudInventoryQueryEngine"/> over test-seeded candidates, so Backend endpoint tests
/// exercise genuine authorization/category/sort/page behavior without requiring a real MariaDB (the
/// database-backed candidate query itself is proven separately by
/// ACE.Cloud.PersistenceIntegrationTests.CloudInventoryQueryReaderTests).
/// </summary>
internal sealed class FakeCloudInventoryQueryReader : ICloudInventoryQueryReader
{
    public List<CloudInventoryQueryCandidate> Candidates { get; } = [];

    public long AsOfCustodyOutboxSequenceNumber { get; set; }

    public Task<CloudInventoryQueryResponse> QueryAsync(
        string shardId, CloudLiveStreamViewer viewer, CloudInventoryQueryRequest request, CancellationToken cancellationToken = default)
    {
        var authorized = CloudInventoryQueryEngine.Authorize(Candidates, viewer);
        var page = CloudInventoryQueryEngine.Query(authorized, request.Category, request.Page, request.SortKey, request.SortDirection);
        return Task.FromResult(new CloudInventoryQueryResponse(page, AsOfCustodyOutboxSequenceNumber));
    }

    public Task<bool> IsItemVisibleToViewerAsync(
        string shardId, CloudLiveStreamViewer viewer, CloudItemId itemId, CancellationToken cancellationToken = default)
    {
        var authorizedOwnerIds = viewer.AuthorizedOwnerIds;
        var isVisible = Candidates.Any(candidate =>
            candidate.ItemId == itemId && (viewer.IsAdmin || authorizedOwnerIds.Contains(candidate.OwnerId)));
        return Task.FromResult(isVisible);
    }
}
