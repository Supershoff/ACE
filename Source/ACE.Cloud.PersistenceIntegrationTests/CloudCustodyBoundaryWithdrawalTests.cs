using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #4's withdrawal half of the crash-safe idempotent world-boundary
/// handoff (ARCH-002, ARCH-006, transaction rules 2-8): custody release, world-possession grant,
/// stale-version rejection, concurrent-redemption serialization, and idempotent replay.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodyBoundaryWithdrawalTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 300_000;

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
    public async Task Withdraw_RestoresWorldPossession_AndReleasesCustody_AndAppendsLedgerAndOutbox()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
        var custodyRecordId = depositOutcome.Value!.Id;

        var withdrawOutcome = await boundary.WithdrawAsync(custodyRecordId, expectedVersion: 1, recipientContainerId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, withdrawOutcome.Kind);
        Assert.AreEqual(biotaId, withdrawOutcome.Value!.BiotaId);
        Assert.AreEqual(recipientContainerId, withdrawOutcome.Value!.RecipientContainerId);
        Assert.AreEqual(ownerId, withdrawOutcome.Value!.FormerOwnerId);

        Assert.IsTrue(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));

        var ledgerEvents = await verifyContext.CloudActivityLedgerEvents
            .Where(e => e.BiotaId == biotaId)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync();
        Assert.HasCount(2, ledgerEvents, "Deposit and Withdrawal must each append exactly one ledger event.");
        Assert.AreEqual(CloudBoundaryOperationType.Deposit, ledgerEvents[0].EventType);
        Assert.AreEqual(CloudBoundaryOperationType.Withdrawal, ledgerEvents[1].EventType);
        Assert.IsTrue(ledgerEvents.All(e => e.Outcome == CloudBoundaryOutcomeKind.Committed));

        var outboxEvents = await verifyContext.CloudCustodyOutboxEvents.Where(e => e.BiotaId == biotaId).ToListAsync();
        Assert.HasCount(2, outboxEvents, "Deposit and Withdrawal must each append exactly one outbox event.");
    }

    [TestMethod]
    public async Task Withdraw_WithStaleExpectedVersion_ReturnsConflict_AndDoesNotMutateAnything()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        var custodyRecordId = depositOutcome.Value!.Id;

        var outcome = await boundary.WithdrawAsync(custodyRecordId, expectedVersion: 999, NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "version");

        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));
        Assert.AreEqual(0, await verifyContext.CloudActivityLedgerEvents.CountAsync(e => e.EventType == CloudBoundaryOperationType.Withdrawal));
    }

    [TestMethod]
    public async Task Withdraw_UnknownCustodyRecordId_ReturnsConflict_WithoutThrowing()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.WithdrawAsync(Guid.NewGuid(), expectedVersion: 1, NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task ConcurrentWithdrawals_ForSameCustodyRecord_OnlyOneSucceeds()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid custodyRecordId;
        await using (var seedContext = new CloudDbContext(options))
        {
            var seedBoundary = new CloudCustodyBoundary(seedContext);
            var depositOutcome = await seedBoundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            custodyRecordId = depositOutcome.Value!.Id;
        }

        var containerA = NextId();
        var containerB = NextId();

        await using var contextA = new CloudDbContext(options);
        await using var contextB = new CloudDbContext(options);
        var boundaryA = new CloudCustodyBoundary(contextA);
        var boundaryB = new CloudCustodyBoundary(contextB);

        var taskA = boundaryA.WithdrawAsync(custodyRecordId, expectedVersion: 1, containerA, Guid.NewGuid());
        var taskB = boundaryB.WithdrawAsync(custodyRecordId, expectedVersion: 1, containerB, Guid.NewGuid());

        var results = await Task.WhenAll(taskA, taskB);

        var committedCount = results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Committed);
        var conflictCount = results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Conflict);
        Assert.AreEqual(1, committedCount, "Deterministic row locking must let exactly one concurrent redemption win.");
        Assert.AreEqual(1, conflictCount);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));

        var hasContainerA = await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, biotaId, containerA);
        var hasContainerB = await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, biotaId, containerB);
        Assert.AreNotEqual(hasContainerA, hasContainerB, "Exactly one recipient container must have received the biota, never both.");
    }

    [TestMethod]
    public async Task ConcurrentWithdrawals_WithTheSameIdempotencyKey_BothReplayTheCommittedResult()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid custodyRecordId;
        await using (var seedContext = new CloudDbContext(options))
        {
            var seedBoundary = new CloudCustodyBoundary(seedContext);
            var depositOutcome = await seedBoundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
            custodyRecordId = depositOutcome.Value!.Id;
        }

        var recipientContainerId = NextId();

        await using var contextA = new CloudDbContext(options);
        await using var contextB = new CloudDbContext(options);
        var boundaryA = new CloudCustodyBoundary(contextA);
        var boundaryB = new CloudCustodyBoundary(contextB);

        var idempotencyKey = Guid.NewGuid();

        // Both concurrent calls share the same idempotency key and target custody record, modeling
        // a caller that retries a slow-but-not-yet-failed withdrawal (transaction rules 4 and 8):
        // the loser must replay the winner's committed result, not report a domain Conflict.
        var taskA = boundaryA.WithdrawAsync(custodyRecordId, expectedVersion: 1, recipientContainerId, idempotencyKey);
        var taskB = boundaryB.WithdrawAsync(custodyRecordId, expectedVersion: 1, recipientContainerId, idempotencyKey);

        var results = await Task.WhenAll(taskA, taskB);

        Assert.IsTrue(
            results.All(r => r.Kind == CloudBoundaryOutcomeKind.Committed),
            "Same-key concurrent withdrawals must both observe the committed replay, never a spurious Conflict.");
        Assert.AreEqual(biotaId, results[0].Value!.BiotaId);
        Assert.AreEqual(biotaId, results[1].Value!.BiotaId);
        Assert.AreEqual(recipientContainerId, results[0].Value!.RecipientContainerId);
        Assert.AreEqual(recipientContainerId, results[1].Value!.RecipientContainerId);

        var containerRowCount = await AceShardTestData.CountContainerRowsAsync(_fixture.AceShardConnectionString, biotaId);
        Assert.AreEqual(1, containerRowCount, "Replaying a committed withdrawal must not grant world possession a second time.");
    }

    [TestMethod]
    public async Task RepeatedIdempotencyKey_ForWithdrawal_ReplaysCommittedResult_WithoutReapplyingTheGrant()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        var custodyRecordId = depositOutcome.Value!.Id;
        var recipientContainerId = NextId();
        var idempotencyKey = Guid.NewGuid();

        var first = await boundary.WithdrawAsync(custodyRecordId, expectedVersion: 1, recipientContainerId, idempotencyKey);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.WithdrawAsync(custodyRecordId, expectedVersion: 1, recipientContainerId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.BiotaId, second.Value!.BiotaId);
        Assert.AreEqual(first.Value!.RecipientContainerId, second.Value!.RecipientContainerId);

        var containerRowCount = await AceShardTestData.CountContainerRowsAsync(_fixture.AceShardConnectionString, biotaId);
        Assert.AreEqual(1, containerRowCount, "Replaying a committed withdrawal must not grant world possession a second time.");
    }

    [TestMethod]
    public async Task IdempotencyKey_ReusedAcrossOperationTypes_ReturnsConflict_InsteadOfMisreplaying()
    {
        var depositBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, depositBiotaId);

        var withdrawBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, withdrawBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var sharedKey = Guid.NewGuid();

        var depositOutcome = await boundary.DepositAsync(depositBiotaId, ShardId, Guid.NewGuid(), sharedKey);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);

        var unrelatedDeposit = await boundary.DepositAsync(withdrawBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        var custodyRecordId = unrelatedDeposit.Value!.Id;

        var withdrawOutcome = await boundary.WithdrawAsync(custodyRecordId, expectedVersion: 1, NextId(), sharedKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, withdrawOutcome.Kind);
        StringAssert.Contains(withdrawOutcome.Reason, "Deposit");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
