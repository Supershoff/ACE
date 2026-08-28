using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Crash-safety coverage for issue #14's acceptance criterion "MMD creation and remainder changes
/// are idempotent under retry/crash," mirroring <see cref="CloudCustodyBoundaryFaultInjectionTests"/>'s
/// established pattern for the two new operations: every injected crash before commit must leave
/// exactly the pre-transaction state (no partial remainder update, no orphaned MMD custody record,
/// no consumed-but-unconverted raw biota), and a crash after commit must leave the already-committed
/// result replayable rather than duplicated.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudPyrealBoundaryFaultInjectionTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 900_000;

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

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    [TestMethod]
    public async Task CrashBeforeLocks_DuringConversion_LeavesNothingCommitted() =>
        await AssertConversionCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.BeforeLocks);

    [TestMethod]
    public async Task CrashAfterValidation_DuringConversion_LeavesNothingCommitted() =>
        await AssertConversionCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterValidation);

    [TestMethod]
    public async Task CrashAfterCustodyChange_DuringConversion_LeavesNothingCommitted() =>
        await AssertConversionCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterCustodyChange);

    [TestMethod]
    public async Task CrashAfterLedgerAppend_DuringConversion_LeavesNothingCommitted() =>
        await AssertConversionCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterLedgerAppend);

    [TestMethod]
    public async Task CrashAfterOutboxAppend_DuringConversion_LeavesNothingCommitted() =>
        await AssertConversionCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterOutboxAppend);

    [TestMethod]
    public async Task CrashBeforeCommit_DuringConversion_LeavesNothingCommitted() =>
        await AssertConversionCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.BeforeCommit);

    [TestMethod]
    public async Task CrashAfterCommit_DuringConversion_LeavesTheCommittedResultReplayable_NotDuplicated()
    {
        var rawBiotaId = NextId();
        var mmdBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, rawBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, rawBiotaId, PyrealConversionPolicy.PyrealsPerMmd);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, mmdBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        Func<CloudBoundaryFaultPoint, Task> crashAfterCommit = point =>
            point == CloudBoundaryFaultPoint.AfterCommit
                ? throw new CloudBoundarySimulatedCrashException(point)
                : Task.CompletedTask;

        // The caller "crashes" after MariaDB already committed: it never observes a return value.
        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(() => boundary.ConvertPyrealDepositAsync(
            rawBiotaId, ShardId, ownerId, PyrealConversionPolicy.PyrealsPerMmd, [mmdBiotaId], idempotencyKey, crashAfterCommit, CancellationToken.None));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == mmdBiotaId), "The commit that already happened must be the sole authoritative state.");
        Assert.AreEqual(1, await verifyContext.CloudPyrealConversionRecords.CountAsync(r => r.IdempotencyKey == idempotencyKey));
        Assert.AreEqual(0, (await verifyContext.CloudPyrealRemainders.AsNoTracking().SingleAsync(r => r.OwnerId == ownerId)).RemainderAmount);

        // A "restarted caller" retries with the same idempotency key: it must replay the already
        // committed result, not convert the (now-deleted) raw biota again.
        await using var retryContext = new CloudDbContext(options);
        var retryBoundary = new CloudCustodyBoundary(retryContext);
        var replay = await retryBoundary.ConvertPyrealDepositAsync(
            rawBiotaId, ShardId, ownerId, PyrealConversionPolicy.PyrealsPerMmd, [mmdBiotaId], idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, replay.Kind);
        Assert.AreEqual(mmdBiotaId, replay.Value!.MmdCustodyRecords[0].BiotaId);

        await using var finalVerifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await finalVerifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == mmdBiotaId), "Replay must not reapply the conversion.");
    }

    [TestMethod]
    public async Task CrashBeforeLocks_DuringPyrealRemainderWithdrawal_LeavesNothingCommitted() =>
        await AssertWithdrawalCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.BeforeLocks);

    [TestMethod]
    public async Task CrashAfterValidation_DuringPyrealRemainderWithdrawal_LeavesNothingCommitted() =>
        await AssertWithdrawalCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterValidation);

    [TestMethod]
    public async Task CrashAfterPossessionChange_DuringPyrealRemainderWithdrawal_LeavesNothingCommitted() =>
        await AssertWithdrawalCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterPossessionChange);

    [TestMethod]
    public async Task CrashAfterLedgerAppend_DuringPyrealRemainderWithdrawal_LeavesNothingCommitted() =>
        await AssertWithdrawalCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.AfterLedgerAppend);

    [TestMethod]
    public async Task CrashBeforeCommit_DuringPyrealRemainderWithdrawal_LeavesNothingCommitted() =>
        await AssertWithdrawalCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint.BeforeCommit);

    [TestMethod]
    public async Task CrashAfterCommit_DuringPyrealRemainderWithdrawal_LeavesTheCommittedResultReplayable_NotDuplicated()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        await SeedRemainderAsync(options, ownerId, 5_000);

        var deliveryBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, deliveryBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, deliveryBiotaId, 5_000);
        var recipientContainerId = NextId();
        var idempotencyKey = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        Func<CloudBoundaryFaultPoint, Task> crashAfterCommit = point =>
            point == CloudBoundaryFaultPoint.AfterCommit
                ? throw new CloudBoundarySimulatedCrashException(point)
                : Task.CompletedTask;

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(() => boundary.WithdrawPyrealRemainderAsync(
            ShardId, ownerId, 5_000, [deliveryBiotaId], recipientContainerId, idempotencyKey, crashAfterCommit, CancellationToken.None));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, (await verifyContext.CloudPyrealRemainders.AsNoTracking().SingleAsync(r => r.OwnerId == ownerId)).RemainderAmount);
        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, deliveryBiotaId, recipientContainerId));

        // A "restarted caller" retries with the same idempotency key: it must replay the already
        // committed result, not grant the delivery biota a second time.
        await using var retryContext = new CloudDbContext(options);
        var retryBoundary = new CloudCustodyBoundary(retryContext);
        var replay = await retryBoundary.WithdrawPyrealRemainderAsync(
            ShardId, ownerId, 5_000, [deliveryBiotaId], recipientContainerId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, replay.Kind);
        Assert.AreEqual(1, await AceShardTestData.CountContainerRowsAsync(_fixture.AceShardConnectionString, deliveryBiotaId), "Replay must not re-grant the delivery biota.");
    }

    private async Task AssertConversionCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint faultPoint)
    {
        var rawBiotaId = NextId();
        var mmdBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, rawBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, rawBiotaId, PyrealConversionPolicy.PyrealsPerMmd);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, mmdBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var ownerId = Guid.NewGuid();

        Func<CloudBoundaryFaultPoint, Task> injector = point =>
            point == faultPoint ? throw new CloudBoundarySimulatedCrashException(point) : Task.CompletedTask;

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(() => boundary.ConvertPyrealDepositAsync(
            rawBiotaId, ShardId, ownerId, PyrealConversionPolicy.PyrealsPerMmd, [mmdBiotaId], Guid.NewGuid(), injector, CancellationToken.None));

        await using var verifyContext = new CloudDbContext(options);
        Assert.IsTrue(
            await AceShardTestData.BiotaExistsAsync(_fixture.AceShardConnectionString, rawBiotaId),
            $"Crash at {faultPoint} must not leave the raw Pyreal biota consumed without a committed conversion.");
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == mmdBiotaId), $"Crash at {faultPoint} must roll back the MMD custody insert.");
        Assert.IsFalse(await verifyContext.CloudPyrealRemainders.AnyAsync(r => r.OwnerId == ownerId), $"Crash at {faultPoint} must roll back the remainder update.");
        Assert.AreEqual(0, await verifyContext.CloudPyrealConversionRecords.CountAsync(), $"Crash at {faultPoint} must roll back the conversion record.");
        Assert.AreEqual(0, await verifyContext.CloudActivityLedgerEvents.CountAsync(), $"Crash at {faultPoint} must roll back the ledger append.");
        Assert.AreEqual(0, await verifyContext.CloudCustodyOutboxEvents.CountAsync(), $"Crash at {faultPoint} must roll back the outbox append.");
    }

    private async Task AssertWithdrawalCrashLeavesNothingCommittedAsync(CloudBoundaryFaultPoint faultPoint)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        await SeedRemainderAsync(options, ownerId, 5_000);

        var deliveryBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, deliveryBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, deliveryBiotaId, 5_000);
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        Func<CloudBoundaryFaultPoint, Task> injector = point =>
            point == faultPoint ? throw new CloudBoundarySimulatedCrashException(point) : Task.CompletedTask;

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(() => boundary.WithdrawPyrealRemainderAsync(
            ShardId, ownerId, 5_000, [deliveryBiotaId], recipientContainerId, Guid.NewGuid(), injector, CancellationToken.None));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(
            5_000, (await verifyContext.CloudPyrealRemainders.AsNoTracking().SingleAsync(r => r.OwnerId == ownerId)).RemainderAmount,
            $"Crash at {faultPoint} must leave the Pyreal Remainder exactly unchanged.");
        Assert.IsFalse(
            await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, deliveryBiotaId),
            $"Crash at {faultPoint} must not leave world possession granted without a committed remainder debit.");
        Assert.AreEqual(0, await verifyContext.CloudPyrealRemainderWithdrawalRecords.CountAsync(), $"Crash at {faultPoint} must roll back the withdrawal record.");
    }

    private async Task SeedRemainderAsync(Microsoft.EntityFrameworkCore.DbContextOptions<CloudDbContext> options, Guid ownerId, long amount)
    {
        var rawBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, rawBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, rawBiotaId, amount);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var outcome = await boundary.ConvertPyrealDepositAsync(rawBiotaId, ShardId, ownerId, amount, mmdBiotaIds: [], Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
    }
}
