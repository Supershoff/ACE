using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #5's "retry after each crash point" Red requirement, extending
/// issue #4's <see cref="CloudCustodyBoundaryFaultInjectionTests"/> pattern to
/// <see cref="CloudCustodyBoundary.WithdrawLotAsync"/>: a simulated crash at every named
/// <see cref="CloudBoundaryFaultPoint"/> during a materializing partial withdrawal must leave
/// exactly one authoritative custody state, and a retry with the same idempotency key afterward
/// must reach the same committed result without double-materializing or losing quantity.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStackLotMaterializationFaultInjectionTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 1_000_000;

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

    [TestMethod]
    public async Task CrashBeforeLocks_DuringLotWithdrawal_LeavesNothingCommitted() =>
        await AssertCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.BeforeLocks);

    [TestMethod]
    public async Task CrashAfterValidation_DuringLotWithdrawal_LeavesNothingCommitted() =>
        await AssertCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterValidation);

    [TestMethod]
    public async Task CrashAfterCustodyChange_DuringLotWithdrawal_LeavesNothingCommitted() =>
        await AssertCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterCustodyChange);

    [TestMethod]
    public async Task CrashAfterPossessionChange_DuringLotWithdrawal_LeavesNothingCommitted() =>
        await AssertCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterPossessionChange);

    [TestMethod]
    public async Task CrashAfterLedgerAppend_DuringLotWithdrawal_LeavesNothingCommitted() =>
        await AssertCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterLedgerAppend);

    [TestMethod]
    public async Task CrashAfterOutboxAppend_DuringLotWithdrawal_LeavesNothingCommitted() =>
        await AssertCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterOutboxAppend);

    [TestMethod]
    public async Task CrashBeforeCommit_DuringLotWithdrawal_LeavesNothingCommitted() =>
        await AssertCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.BeforeCommit);

    [TestMethod]
    public async Task CrashAfterCommit_DuringLotWithdrawal_LeavesTheCommittedResultReplayable_NotDuplicated()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var materializedBiotaId = NextId();
        var recipientContainerId = NextId();
        var idempotencyKey = Guid.NewGuid();

        Guid lotId;
        int lotVersion;
        await using (var seedContext = new CloudDbContext(options))
        {
            var seedBoundary = new CloudCustodyBoundary(seedContext);
            var depositOutcome = await seedBoundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 25, Guid.NewGuid());
            lotId = depositOutcome.Value!.Lot.Id;
            lotVersion = depositOutcome.Value!.Lot.Version;
        }

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        Func<CloudBoundaryFaultPoint, Task> crashAfterCommit = point =>
            point == CloudBoundaryFaultPoint.AfterCommit
                ? throw new CloudBoundarySimulatedCrashException(point)
                : Task.CompletedTask;

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(
            () => boundary.WithdrawLotAsync(lotId, lotVersion, 10, recipientContainerId, materializedBiotaId, idempotencyKey, crashAfterCommit, CancellationToken.None));

        var childStackSizeAfterCrash = await AceShardTestData.GetStackSizeAsync(_fixture.AceShardConnectionString, materializedBiotaId);
        Assert.AreEqual(10, childStackSizeAfterCrash, "The commit that already happened must be the sole authoritative state.");

        await using var retryContext = new CloudDbContext(options);
        var retryBoundary = new CloudCustodyBoundary(retryContext);
        var replay = await retryBoundary.WithdrawLotAsync(lotId, lotVersion, 10, recipientContainerId, materializedBiotaId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, replay.Kind);
        Assert.AreEqual(materializedBiotaId, replay.Value!.DeliveredBiotaId);

        await using var finalVerifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await finalVerifyContext.CloudStackLotLineageEvents.CountAsync(e => e.ChildBiotaId == materializedBiotaId), "Replay must not re-log a second lineage event.");

        var childStackSizeAfterReplay = await AceShardTestData.GetStackSizeAsync(_fixture.AceShardConnectionString, materializedBiotaId);
        Assert.AreEqual(10, childStackSizeAfterReplay, "Replay must not re-apply the materialization a second time.");
    }

    private async Task AssertCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint faultPoint)
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var materializedBiotaId = NextId();

        Guid lotId;
        int lotVersion;
        await using (var seedContext = new CloudDbContext(options))
        {
            var seedBoundary = new CloudCustodyBoundary(seedContext);
            var depositOutcome = await seedBoundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 25, Guid.NewGuid());
            lotId = depositOutcome.Value!.Lot.Id;
            lotVersion = depositOutcome.Value!.Lot.Version;
        }

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        Func<CloudBoundaryFaultPoint, Task> injector = point =>
            point == faultPoint ? throw new CloudBoundarySimulatedCrashException(point) : Task.CompletedTask;

        var withdrawalIdempotencyKey = Guid.NewGuid();

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(
            () => boundary.WithdrawLotAsync(lotId, lotVersion, 10, NextId(), materializedBiotaId, withdrawalIdempotencyKey, injector, CancellationToken.None));

        // Exactly one authoritative state survives the crash: the lot is untouched...
        await using var verifyContext = new CloudDbContext(options);
        var lot = await verifyContext.CloudStackLots.AsNoTracking().SingleOrDefaultAsync(l => l.Id == lotId);
        Assert.IsNotNull(lot, $"Crash at {faultPoint} must leave the original lot in place.");
        Assert.AreEqual(25, lot!.Quantity, $"Crash at {faultPoint} must roll back any quantity change.");

        // ...no child biota/property survived a rolled-back transaction...
        Assert.IsFalse(await AceShardTestData.BiotaExistsAsync(_fixture.AceShardConnectionString, materializedBiotaId), $"Crash at {faultPoint} must not leave a materialized child biota behind.");

        // ...and no lineage event or idempotency record leaked out either (the seed deposit's own
        // idempotency record legitimately survives, so this checks only the withdrawal's key).
        Assert.AreEqual(0, await verifyContext.CloudStackLotLineageEvents.CountAsync(e => e.ChildBiotaId == materializedBiotaId), $"Crash at {faultPoint} must roll back the lineage event.");
        Assert.AreEqual(0, await verifyContext.CloudIdempotencyRecords.CountAsync(r => r.IdempotencyKey == withdrawalIdempotencyKey), $"Crash at {faultPoint} must roll back the idempotency record.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
