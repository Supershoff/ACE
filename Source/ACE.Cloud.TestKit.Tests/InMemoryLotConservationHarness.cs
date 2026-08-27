using ACE.Cloud.TestKit;

namespace ACE.Cloud.TestKit.Tests;

/// <summary>Mutable in-memory analogue of a CloudStackLot row: quantity, owner, and a version.</summary>
public sealed class InMemoryLot
{
    public Guid Id { get; } = Guid.NewGuid();

    public int Version { get; set; } = 1;

    public Guid OwnerId { get; set; }

    public int Quantity { get; set; }

    public InMemoryLot(Guid ownerId, int quantity)
    {
        OwnerId = ownerId;
        Quantity = quantity;
    }
}

/// <summary>
/// A minimal, storage-agnostic reference implementation of
/// <see cref="ICloudLotConservationHarness{TLotId, TOwnerId}"/>, mirroring
/// <c>CloudStackLotTransactionAuthority</c>'s split/merge/transfer semantics without a database.
/// </summary>
public sealed class InMemoryLotConservationHarness : ICloudLotConservationHarness<Guid, Guid>
{
    private readonly object _gate = new();
    private readonly List<InMemoryLot> _lots;

    public InMemoryLotConservationHarness(int totalQuantity)
    {
        TotalQuantity = totalQuantity;
        _lots = [new InMemoryLot(Guid.NewGuid(), totalQuantity)];
    }

    public int TotalQuantity { get; }

    public Task<IReadOnlyList<CloudLotSnapshot<Guid, Guid>>> GetLotsAsync()
    {
        lock (_gate)
        {
            IReadOnlyList<CloudLotSnapshot<Guid, Guid>> snapshot =
                _lots.Select(l => new CloudLotSnapshot<Guid, Guid>(l.Id, l.Version, l.OwnerId, l.Quantity)).ToList();
            return Task.FromResult(snapshot);
        }
    }

    public Task<bool> SplitAsync(Guid lotId, int expectedVersion, Guid newOwnerId, int quantity)
    {
        lock (_gate)
        {
            var lot = _lots.SingleOrDefault(l => l.Id == lotId);
            if (lot is null || lot.Version != expectedVersion || quantity <= 0 || quantity >= lot.Quantity)
            {
                return Task.FromResult(false);
            }

            lot.Quantity -= quantity;
            lot.Version++;
            _lots.Add(new InMemoryLot(newOwnerId, quantity));
            return Task.FromResult(true);
        }
    }

    public Task<bool> MergeAsync(Guid keepLotId, int expectedKeepVersion, Guid mergeLotId, int expectedMergeVersion)
    {
        lock (_gate)
        {
            var keep = _lots.SingleOrDefault(l => l.Id == keepLotId);
            var merge = _lots.SingleOrDefault(l => l.Id == mergeLotId);
            if (keep is null || merge is null || keep.Version != expectedKeepVersion || merge.Version != expectedMergeVersion || keep.OwnerId != merge.OwnerId)
            {
                return Task.FromResult(false);
            }

            keep.Quantity += merge.Quantity;
            keep.Version++;
            _lots.Remove(merge);
            return Task.FromResult(true);
        }
    }

    public Task<bool> TransferAsync(Guid lotId, int expectedVersion, Guid newOwnerId)
    {
        lock (_gate)
        {
            var lot = _lots.SingleOrDefault(l => l.Id == lotId);
            if (lot is null || lot.Version != expectedVersion)
            {
                return Task.FromResult(false);
            }

            lot.OwnerId = newOwnerId;
            lot.Version++;
            return Task.FromResult(true);
        }
    }

    public Guid NewOwnerId() => Guid.NewGuid();
}
