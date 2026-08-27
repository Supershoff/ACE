using ACE.Cloud.TestKit;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Adopts <see cref="CloudLotConservationInvariantSuite{TLotId, TOwnerId}"/> against the real
/// <see cref="ACE.Cloud.Persistence.CloudStackLotTransactionAuthority"/> and a disposable MariaDB
/// instance, with zero test logic of its own beyond wiring
/// <see cref="PersistenceLotConservationHarness"/>. See
/// <see cref="PersistenceIdempotentCommandInvariantSuiteTests"/> for why this proves issue #10's
/// "adopt without copying" acceptance criterion. This complements, rather than replaces,
/// <see cref="CloudStackLotConservationPropertyTests"/>'s original 200-step/3-seed coverage from
/// issue #5.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PersistenceLotConservationInvariantSuiteTests : CloudLotConservationInvariantSuite<Guid, Guid>
{
    private const string ShardId = "us1";
    private const int TotalQuantity = 1_000;

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 730_000;

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

    protected override ICloudLotConservationHarness<Guid, Guid> CreateHarness() =>
        new PersistenceLotConservationHarness(_fixture, ShardId, NextBiotaId(), TotalQuantity);

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
