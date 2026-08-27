using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>A snapshot of one freshly deposited Cloud Stack Lot's identity and starting version.</summary>
public sealed record PersistenceLotState(Guid LotId, int Version);

/// <summary>
/// Adapts <see cref="CloudStackLotTransactionAuthority.TransferLotAsync"/> to
/// <see cref="ICloudOptimisticConflictHarness{TState}"/>, proving the shared optimistic-conflict
/// suite (issue #10) is usable by a real MariaDB-backed adapter. Public (rather than internal)
/// because <see cref="CloudOptimisticConflictInvariantSuite{TState}.CreateHarness"/> is protected,
/// and C# requires a protected member's return type to be at least as accessible as the member.
/// </summary>
public sealed class PersistenceOptimisticConflictHarness : ICloudOptimisticConflictHarness<PersistenceLotState>
{
    private readonly CloudDatabaseFixture _fixture;
    private readonly string _shardId;
    private readonly Func<uint> _nextBiotaId;

    public PersistenceOptimisticConflictHarness(CloudDatabaseFixture fixture, string shardId, Func<uint> nextBiotaId)
    {
        _fixture = fixture;
        _shardId = shardId;
        _nextBiotaId = nextBiotaId;
    }

    public async Task<PersistenceLotState> ArrangeAsync()
    {
        var biotaId = _nextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, _shardId, Guid.NewGuid(), quantity: 10, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;
        return new PersistenceLotState(lot.Id, lot.Version);
    }

    public int VersionOf(PersistenceLotState state) => state.Version;

    public async Task<bool> TryMutateAsync(PersistenceLotState state, int expectedVersion)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudStackLotTransactionAuthority(context);

        var outcome = await authority.TransferLotAsync(state.LotId, expectedVersion, Guid.NewGuid());
        return outcome.Kind == CloudBoundaryOutcomeKind.Committed;
    }
}
