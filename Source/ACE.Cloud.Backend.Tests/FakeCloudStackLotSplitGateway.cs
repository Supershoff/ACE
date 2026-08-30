using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudStackLotSplitGateway"/> substitute.</summary>
internal sealed class FakeCloudStackLotSplitGateway : ICloudStackLotSplitGateway
{
    private readonly Dictionary<Guid, CloudStackLotSnapshot> _snapshotsByLotId = [];

    public CloudBoundaryOutcome<CloudStackLotSplitResult>? NextSplitOutcome { get; set; }

    public void Seed(Guid lotId, CloudStackLotSnapshot snapshot) => _snapshotsByLotId[lotId] = snapshot;

    public Task<CloudBoundaryOutcome<CloudStackLotSplitResult>> SplitLotAsync(
        Guid lotId, int expectedVersion, Guid newOwnerId, int quantityToSplit, CancellationToken cancellationToken = default) =>
        Task.FromResult(NextSplitOutcome ?? CloudBoundaryOutcome<CloudStackLotSplitResult>.Conflict("No split outcome configured by this test."));

    public Task<CloudStackLotSnapshot?> TryGetLotSnapshotAsync(Guid lotId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshotsByLotId.GetValueOrDefault(lotId));
}
