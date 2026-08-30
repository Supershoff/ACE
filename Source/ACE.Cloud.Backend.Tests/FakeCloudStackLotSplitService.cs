using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>An in-memory <see cref="ICloudStackLotSplitService"/> substitute for endpoint tests.</summary>
internal sealed class FakeCloudStackLotSplitService : ICloudStackLotSplitService
{
    public string? NextConflictReason { get; set; }

    public Guid? LastOwnerId { get; private set; }

    public Task<CloudBoundaryOutcome<CloudStackLotSplitResult>> SplitOwnLotAsync(
        Guid lotId, int expectedVersion, Guid ownerId, int quantityToSplit, CancellationToken cancellationToken = default)
    {
        LastOwnerId = ownerId;

        if (NextConflictReason is { } reason)
        {
            return Task.FromResult(CloudBoundaryOutcome<CloudStackLotSplitResult>.Conflict(reason));
        }

        var custodyRecordId = Guid.NewGuid();
        var remainingLot = new CloudStackLot(custodyRecordId, "us1", ownerId, 1);
        var newLot = new CloudStackLot(custodyRecordId, "us1", ownerId, quantityToSplit);

        return Task.FromResult(CloudBoundaryOutcome<CloudStackLotSplitResult>.Committed(new CloudStackLotSplitResult(remainingLot, newLot)));
    }
}
