using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>A snapshot of one freshly arranged whole Cloud Item's custody identity and starting version.</summary>
public sealed record PersistenceOwnershipState(uint BiotaId, int Version);

/// <summary>
/// Adapts <see cref="CloudOwnershipTransferAuthority.TransferAsync(uint, Guid, int, Guid, CancellationToken)"/>
/// to <see cref="ICloudOptimisticConflictHarness{TState}"/>, proving the shared optimistic-conflict
/// suite (issue #10, adopted for issue #21's "ownership transfers" adapter category) is usable by a
/// real MariaDB-backed adapter for a whole-item Cloud ownership transfer, not only a Cloud Stack Lot
/// (<see cref="PersistenceOptimisticConflictHarness"/>). Public (rather than internal) for the same
/// reason <see cref="PersistenceOptimisticConflictHarness"/> is: <see cref="CloudOptimisticConflictInvariantSuite{TState}.CreateHarness"/>
/// is protected, and C# requires a protected member's return type to be at least as accessible as
/// the member.
/// </summary>
public sealed class PersistenceOwnershipTransferOptimisticConflictHarness : ICloudOptimisticConflictHarness<PersistenceOwnershipState>
{
    private readonly CloudDatabaseFixture _fixture;
    private readonly string _shardId;
    private readonly Func<uint> _nextBiotaId;

    public PersistenceOwnershipTransferOptimisticConflictHarness(CloudDatabaseFixture fixture, string shardId, Func<uint> nextBiotaId)
    {
        _fixture = fixture;
        _shardId = shardId;
        _nextBiotaId = nextBiotaId;
    }

    public async Task<PersistenceOwnershipState> ArrangeAsync()
    {
        var biotaId = _nextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var record = new CloudCustodyRecord(biotaId, _shardId, Guid.NewGuid(), Guid.NewGuid());
        context.CloudCustodyRecords.Add(record);
        await context.SaveChangesAsync();

        return new PersistenceOwnershipState(biotaId, record.Version);
    }

    public int VersionOf(PersistenceOwnershipState state) => state.Version;

    public async Task<bool> TryMutateAsync(PersistenceOwnershipState state, int expectedVersion)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        var outcome = await authority.TransferAsync(state.BiotaId, Guid.NewGuid(), expectedVersion, Guid.NewGuid());
        return outcome.Kind == CloudBoundaryOutcomeKind.Committed;
    }
}
