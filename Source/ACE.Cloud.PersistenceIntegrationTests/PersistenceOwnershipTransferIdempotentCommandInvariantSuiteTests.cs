using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adopts <see cref="CloudIdempotentCommandInvariantSuite{TEffect}"/> against the real
/// <see cref="CloudOwnershipTransferAuthority"/> and a disposable MariaDB instance, with zero test
/// logic of its own beyond wiring <see cref="PersistenceOwnershipTransferIdempotentCommandHarness"/>
/// (issue #21's "ownership transfers" and "repeated idempotency keys" Red section categories).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PersistenceOwnershipTransferIdempotentCommandInvariantSuiteTests : CloudIdempotentCommandInvariantSuite<CloudCustodyRecord>
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 780_000;

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

    protected override ICloudIdempotentCommandHarness<CloudCustodyRecord> CreateHarness() =>
        new PersistenceOwnershipTransferIdempotentCommandHarness(_fixture, ShardId, NextBiotaId);

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
