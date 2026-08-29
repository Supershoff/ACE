using ACE.Cloud.TestKit;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adopts <see cref="CloudLedgerOutboxAtomicityInvariantSuite"/> against the real
/// <see cref="ACE.Cloud.Persistence.CloudOwnershipTransferAuthority"/> and a disposable MariaDB
/// instance, with zero test logic of its own beyond wiring
/// <see cref="PersistenceOwnershipTransferLedgerOutboxAtomicityHarness"/> (issue #21's "ownership
/// transfers" Red section category run against the shared adapter suites, matching
/// <see cref="PersistenceLedgerOutboxAtomicityInvariantSuiteTests"/>'s established Deposit coverage).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PersistenceOwnershipTransferLedgerOutboxAtomicityInvariantSuiteTests : CloudLedgerOutboxAtomicityInvariantSuite
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 760_000;

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

    protected override ICloudLedgerOutboxAtomicityHarness CreateHarness() =>
        new PersistenceOwnershipTransferLedgerOutboxAtomicityHarness(_fixture, ShardId, NextBiotaId);

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
