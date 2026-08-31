using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// An in-memory <see cref="ICloudActivityLedgerQueryReader"/> substitute that still runs the real,
/// pure <see cref="CloudActivityLedgerQueryEngine"/> over test-seeded candidates, mirroring
/// <c>FakeCloudInventoryQueryReader</c>'s exact shape.
/// </summary>
internal sealed class FakeCloudActivityLedgerQueryReader : ICloudActivityLedgerQueryReader
{
    public List<CloudActivityLedgerEntry> Candidates { get; } = [];

    public Task<CloudActivityLedgerPage> QueryAsync(
        string shardId, CloudLiveStreamViewer viewer, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        var authorized = CloudActivityLedgerQueryEngine.Authorize(Candidates, viewer);
        return Task.FromResult(CloudActivityLedgerQueryEngine.Paginate(authorized, pageNumber, pageSize));
    }
}
