using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.TestKit;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adopts <see cref="CloudEventConsumptionInvariantSuite{TPayload}"/> against the real
/// <see cref="ACE.Cloud.Persistence.CloudInventoryReadProjection"/> row and a disposable MariaDB
/// instance, with zero test logic of its own beyond wiring
/// <see cref="PersistenceCustodyProjectionEventConsumptionHarness"/> (issue #22's Red "duplicate,
/// delayed, out-of-order... events" requirement).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PersistenceCustodyProjectionEventConsumptionInvariantSuiteTests : CloudEventConsumptionInvariantSuite<CloudCustodyOutboxEventPayload>
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 880_000;

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

    protected override ICloudEventConsumptionHarness<CloudCustodyOutboxEventPayload> CreateHarness() =>
        new PersistenceCustodyProjectionEventConsumptionHarness(_fixture, ShardId, NextBiotaId());

    // The harness applies every event to the fixed BiotaId it was constructed with rather than
    // whatever ItemId a payload happens to carry, so this ItemId's exact value is arbitrary.
    protected override CloudCustodyOutboxEventPayload CreatePayload(int step) =>
        new(new CloudItemId(1), new CloudAccountId(Guid.NewGuid()), $"Step{step}");

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
