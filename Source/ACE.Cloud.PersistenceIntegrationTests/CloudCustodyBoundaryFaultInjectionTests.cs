using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #4's Red section: "Create fault-injection tests for every
/// boundary: before locks, after validation, after possession change, after custody change, after
/// ledger append, after outbox append, before commit, after commit." Each test simulates a process
/// crash at exactly one named <see cref="CloudBoundaryFaultPoint"/> and proves the invariant that
/// motivates this issue: every injected crash leaves exactly one authoritative custody state,
/// never a duplicated or lost one.
///
/// Six points use Deposit (a crash before commit must leave nothing; a crash after commit must
/// leave a replayable committed result). AfterPossessionChange and AfterCustodyChange use
/// Withdrawal instead, because that is the only operation where those two boundaries are distinct,
/// separately observable database writes (world possession grant vs. custody-record release).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodyBoundaryFaultInjectionTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 500_000;

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
    public async Task CrashBeforeLocks_DuringDeposit_LeavesNothingCommitted()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await AssertDepositCrashLeavesNothingCommittedAsync(biotaId, CloudBoundaryFaultPoint.BeforeLocks);
    }

    [TestMethod]
    public async Task CrashAfterValidation_DuringDeposit_LeavesNothingCommitted()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await AssertDepositCrashLeavesNothingCommittedAsync(biotaId, CloudBoundaryFaultPoint.AfterValidation);
    }

    [TestMethod]
    public async Task CrashAfterLedgerAppend_DuringDeposit_LeavesNothingCommitted()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await AssertDepositCrashLeavesNothingCommittedAsync(biotaId, CloudBoundaryFaultPoint.AfterLedgerAppend);
    }

    [TestMethod]
    public async Task CrashAfterOutboxAppend_DuringDeposit_LeavesNothingCommitted()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await AssertDepositCrashLeavesNothingCommittedAsync(biotaId, CloudBoundaryFaultPoint.AfterOutboxAppend);
    }

    [TestMethod]
    public async Task CrashBeforeCommit_DuringDeposit_LeavesNothingCommitted()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await AssertDepositCrashLeavesNothingCommittedAsync(biotaId, CloudBoundaryFaultPoint.BeforeCommit);
    }

    [TestMethod]
    public async Task CrashAfterCommit_DuringDeposit_LeavesTheCommittedResultReplayable_NotDuplicated()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

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
        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(
            () => boundary.DepositAsync(biotaId, ShardId, ownerId, idempotencyKey, crashAfterCommit, CancellationToken.None));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId), "The commit that already happened must be the sole authoritative state.");
        Assert.AreEqual(1, await verifyContext.CloudIdempotencyRecords.CountAsync(r => r.IdempotencyKey == idempotencyKey));
        Assert.AreEqual(1, await verifyContext.CloudActivityLedgerEvents.CountAsync(e => e.BiotaId == biotaId));
        Assert.AreEqual(1, await verifyContext.CloudCustodyOutboxEvents.CountAsync(e => e.BiotaId == biotaId));

        // A "restarted caller" retries with the same idempotency key: it must replay the already
        // committed record, not create a second one (this is the recovery path for AfterCommit).
        await using var retryContext = new CloudDbContext(options);
        var retryBoundary = new CloudCustodyBoundary(retryContext);
        var replay = await retryBoundary.DepositAsync(biotaId, ShardId, ownerId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, replay.Kind);

        await using var finalVerifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await finalVerifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId), "Replay must not reapply the deposit.");
    }

    [TestMethod]
    public async Task CrashAfterCustodyChange_DuringWithdrawal_RollsBackBothTheCustodyReleaseAndAnyPossessionGrant()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await AssertWithdrawalCrashPreservesOriginalCustodyAsync(biotaId, CloudBoundaryFaultPoint.AfterCustodyChange);
    }

    [TestMethod]
    public async Task CrashAfterPossessionChange_DuringWithdrawal_RollsBackBothTheCustodyReleaseAndThePossessionGrant()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await AssertWithdrawalCrashPreservesOriginalCustodyAsync(biotaId, CloudBoundaryFaultPoint.AfterPossessionChange);
    }

    private async Task AssertDepositCrashLeavesNothingCommittedAsync(uint biotaId, CloudBoundaryFaultPoint faultPoint)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        Func<CloudBoundaryFaultPoint, Task> injector = point =>
            point == faultPoint ? throw new CloudBoundarySimulatedCrashException(point) : Task.CompletedTask;

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(
            () => boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid(), injector, CancellationToken.None));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId), $"Crash at {faultPoint} must roll back the custody insert.");
        Assert.AreEqual(0, await verifyContext.CloudActivityLedgerEvents.CountAsync(e => e.BiotaId == biotaId), $"Crash at {faultPoint} must roll back the ledger append.");
        Assert.AreEqual(0, await verifyContext.CloudCustodyOutboxEvents.CountAsync(e => e.BiotaId == biotaId), $"Crash at {faultPoint} must roll back the outbox append.");
        Assert.AreEqual(0, await verifyContext.CloudIdempotencyRecords.CountAsync(), $"Crash at {faultPoint} must roll back the idempotency record.");
    }

    private async Task AssertWithdrawalCrashPreservesOriginalCustodyAsync(uint biotaId, CloudBoundaryFaultPoint faultPoint)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        Guid custodyRecordId;
        await using (var seedContext = new CloudDbContext(options))
        {
            var seedBoundary = new CloudCustodyBoundary(seedContext);
            var depositOutcome = await seedBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
            custodyRecordId = depositOutcome.Value!.Id;
        }

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        Func<CloudBoundaryFaultPoint, Task> injector = point =>
            point == faultPoint ? throw new CloudBoundarySimulatedCrashException(point) : Task.CompletedTask;

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(
            () => boundary.WithdrawAsync(custodyRecordId, expectedVersion: 1, NextId(), Guid.NewGuid(), injector, CancellationToken.None));

        // The whole withdrawal transaction rolled back: the custody record survives unchanged and
        // no recipient container was actually granted world possession.
        await using var verifyContext = new CloudDbContext(options);
        var survivingRecord = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleOrDefaultAsync(r => r.Id == custodyRecordId);
        Assert.IsNotNull(survivingRecord, $"Crash at {faultPoint} must leave the original Cloud Custody Record in place.");
        Assert.AreEqual(1, survivingRecord!.Version);

        Assert.IsFalse(
            await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId),
            $"Crash at {faultPoint} must not leave world possession granted without a committed custody release.");

        Assert.AreEqual(1, await verifyContext.CloudActivityLedgerEvents.CountAsync(e => e.BiotaId == biotaId), "Only the Deposit's ledger event should exist; the Withdrawal never committed.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
