using ACE.Cloud.Persistence;
using ACE.Cloud.TestKit;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adopts <see cref="CloudIdempotentCommandInvariantSuite{TEffect}"/> against the real
/// <see cref="CloudCustodyBoundary"/> and a disposable MariaDB instance, with zero test logic of
/// its own beyond wiring <see cref="PersistenceIdempotentCommandHarness"/> (issue #10's acceptance
/// criterion that adapter projects can adopt the shared invariant suites without copying them).
/// See ACE.Cloud.TestKit.Tests for the same suite adopted by a storage-agnostic in-memory adapter.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PersistenceIdempotentCommandInvariantSuiteTests : CloudIdempotentCommandInvariantSuite<CloudCustodyRecord>
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 700_000;

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
        new PersistenceIdempotentCommandHarness(_fixture, ShardId, NextBiotaId);

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
