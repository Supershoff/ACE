using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adapts <see cref="CloudStackLotTransactionAuthority"/> and its backing deposit to
/// <see cref="ICloudLotConservationHarness{TLotId, TOwnerId}"/>, proving the shared lot-conservation
/// suite (issue #10) is usable by a real MariaDB-backed adapter -- the same production classes issue
/// #5's original, adapter-specific version of this test exercised directly. The initial stack
/// deposit runs lazily on first use so this harness can be constructed synchronously from
/// <c>CreateHarness()</c>.
/// </summary>
internal sealed class PersistenceLotConservationHarness : ICloudLotConservationHarness<Guid, Guid>
{
    private readonly CloudDatabaseFixture _fixture;
    private readonly string _shardId;
    private readonly uint _biotaId;
    private readonly Lazy<Task<Guid>> _custodyRecordId;

    public PersistenceLotConservationHarness(CloudDatabaseFixture fixture, string shardId, uint biotaId, int totalQuantity)
    {
        _fixture = fixture;
        _shardId = shardId;
        _biotaId = biotaId;
        TotalQuantity = totalQuantity;
        _custodyRecordId = new Lazy<Task<Guid>>(InitializeAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public int TotalQuantity { get; }

    public async Task<IReadOnlyList<CloudLotSnapshot<Guid, Guid>>> GetLotsAsync()
    {
        var custodyRecordId = await _custodyRecordId.Value;

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var lots = await context.CloudStackLots
            .Where(l => l.CustodyRecordId == custodyRecordId)
            .OrderBy(l => l.Id)
            .ToListAsync();

        return lots.Select(l => new CloudLotSnapshot<Guid, Guid>(l.Id, l.Version, l.OwnerId, l.Quantity)).ToList();
    }

    public async Task<bool> SplitAsync(Guid lotId, int expectedVersion, Guid newOwnerId, int quantity)
    {
        await _custodyRecordId.Value;

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudStackLotTransactionAuthority(context);

        var outcome = await authority.SplitLotAsync(lotId, expectedVersion, newOwnerId, quantity);
        return outcome.Kind == CloudBoundaryOutcomeKind.Committed;
    }

    public async Task<bool> MergeAsync(Guid keepLotId, int expectedKeepVersion, Guid mergeLotId, int expectedMergeVersion)
    {
        await _custodyRecordId.Value;

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudStackLotTransactionAuthority(context);

        var outcome = await authority.MergeLotsAsync(keepLotId, expectedKeepVersion, mergeLotId, expectedMergeVersion);
        return outcome.Kind == CloudBoundaryOutcomeKind.Committed;
    }

    public async Task<bool> TransferAsync(Guid lotId, int expectedVersion, Guid newOwnerId)
    {
        await _custodyRecordId.Value;

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudStackLotTransactionAuthority(context);

        var outcome = await authority.TransferLotAsync(lotId, expectedVersion, newOwnerId);
        return outcome.Kind == CloudBoundaryOutcomeKind.Committed;
    }

    public Guid NewOwnerId() => Guid.NewGuid();

    private async Task<Guid> InitializeAsync()
    {
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, _biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositStackAsync(_biotaId, _shardId, Guid.NewGuid(), TotalQuantity, Guid.NewGuid());
        if (outcome.Kind != CloudBoundaryOutcomeKind.Committed)
        {
            throw new InvalidOperationException($"Expected a committed stack deposit but observed {outcome.Kind}: {outcome.Reason}");
        }

        return outcome.Value!.CustodyRecord.Id;
    }
}
