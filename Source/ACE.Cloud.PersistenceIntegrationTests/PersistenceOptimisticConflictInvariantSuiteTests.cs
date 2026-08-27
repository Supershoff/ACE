using ACE.Cloud.TestKit;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adopts <see cref="CloudOptimisticConflictInvariantSuite{TState}"/> against the real
/// <see cref="ACE.Cloud.Persistence.CloudStackLotTransactionAuthority"/> and a disposable MariaDB
/// instance, with zero test logic of its own beyond wiring
/// <see cref="PersistenceOptimisticConflictHarness"/>. See
/// <see cref="PersistenceIdempotentCommandInvariantSuiteTests"/> for why this proves issue #10's
/// "adopt without copying" acceptance criterion.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PersistenceOptimisticConflictInvariantSuiteTests : CloudOptimisticConflictInvariantSuite<PersistenceLotState>
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 710_000;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await CloudBoundaryTestFixtureData.ResetAsync(_fixture.CloudConnectionString, ShardId);
    }

    protected override ICloudOptimisticConflictHarness<PersistenceLotState> CreateHarness() =>
        new PersistenceOptimisticConflictHarness(_fixture, ShardId, NextBiotaId);

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
