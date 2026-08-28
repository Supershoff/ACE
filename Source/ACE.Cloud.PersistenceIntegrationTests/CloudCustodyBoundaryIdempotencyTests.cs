using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #4's idempotency and caller-timeout requirements (ARCH-006,
/// transaction rules 4 and 8): repeating a request with the same idempotency key must replay the
/// original committed result instead of reapplying the ownership change, and a caller that timed
/// out waiting for a response must requery rather than infer failure.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodyBoundaryIdempotencyTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 400_000;

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
    public async Task RepeatedIdempotencyKey_ForDeposit_ReplaysCommittedResult_WithoutCreatingASecondRecord()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var ownerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        var first = await boundary.DepositAsync(biotaId, ShardId, ownerId, idempotencyKey);
        var second = await boundary.DepositAsync(biotaId, ShardId, ownerId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.Id, second.Value!.Id, "A repeated idempotency key must replay the same Cloud Custody Record, not create another one.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));
        Assert.AreEqual(1, await verifyContext.CloudIdempotencyRecords.CountAsync(r => r.IdempotencyKey == idempotencyKey));
        Assert.AreEqual(1, await verifyContext.CloudActivityLedgerEvents.CountAsync(e => e.BiotaId == biotaId));
        Assert.AreEqual(1, await verifyContext.CloudCustodyOutboxEvents.CountAsync(e => e.BiotaId == biotaId));
    }

    [TestMethod]
    public async Task ConcurrentDeposits_ForTheSameBiota_OnlyOneCommits()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using var contextA = new CloudDbContext(options);
        await using var contextB = new CloudDbContext(options);
        var boundaryA = new CloudCustodyBoundary(contextA);
        var boundaryB = new CloudCustodyBoundary(contextB);

        var taskA = boundaryA.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        var taskB = boundaryB.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        var results = await Task.WhenAll(taskA, taskB);

        Assert.AreEqual(1, results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Committed), "Two independent idempotency keys racing the same biota must still yield exactly one custody record (INV-001).");
        Assert.AreEqual(1, results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Conflict));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));
    }

    [TestMethod]
    public async Task ConcurrentDeposits_WithTheSameIdempotencyKey_BothReplayTheCommittedResult()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using var contextA = new CloudDbContext(options);
        await using var contextB = new CloudDbContext(options);
        var boundaryA = new CloudCustodyBoundary(contextA);
        var boundaryB = new CloudCustodyBoundary(contextB);

        var ownerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        // Both concurrent calls share the same idempotency key and target biota, modeling a caller
        // that retries a slow-but-not-yet-failed deposit (transaction rules 4 and 8): the loser must
        // replay the winner's committed result, not report a domain Conflict.
        var taskA = boundaryA.DepositAsync(biotaId, ShardId, ownerId, idempotencyKey);
        var taskB = boundaryB.DepositAsync(biotaId, ShardId, ownerId, idempotencyKey);

        var results = await Task.WhenAll(taskA, taskB);

        Assert.IsTrue(
            results.All(r => r.Kind == CloudBoundaryOutcomeKind.Committed),
            "Same-key concurrent deposits must both observe the committed replay, never a spurious Conflict.");
        Assert.AreEqual(
            results[0].Value!.Id, results[1].Value!.Id,
            "Both callers must observe the same Cloud Custody Record.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));
        Assert.AreEqual(1, await verifyContext.CloudIdempotencyRecords.CountAsync(r => r.IdempotencyKey == idempotencyKey));
    }

    [TestMethod]
    public async Task CallerTimeout_MustRequeryTheIdempotencyRecord_InsteadOfInferringFailure()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        await using var workerContext = new CloudDbContext(options);
        var workerBoundary = new CloudCustodyBoundary(workerContext);

        // Simulate a deposit that is slower than the caller is willing to wait: an artificial
        // delay runs inside the "before locks" fault point before the real work starts.
        Func<CloudBoundaryFaultPoint, Task> slowStart = async point =>
        {
            if (point == CloudBoundaryFaultPoint.BeforeLocks)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300));
            }
        };

        var slowDeposit = workerBoundary.DepositAsync(biotaId, ShardId, ownerId, idempotencyKey, slowStart, CancellationToken.None);

        // The caller gives up waiting well before the worker finishes...
        var callerGaveUp = await Task.WhenAny(slowDeposit, Task.Delay(TimeSpan.FromMilliseconds(50))) != slowDeposit;
        Assert.IsTrue(callerGaveUp, "This test requires the worker to still be running when the caller times out.");

        // ...transaction rule 8: the timed-out caller must requery the idempotency record rather
        // than assume the deposit failed. Poll until the worker actually commits.
        await using var callerContext = new CloudDbContext(options);
        var callerBoundary = new CloudCustodyBoundary(callerContext);

        CloudBoundaryOutcome<CloudCustodyRecord>? requeried = null;
        for (var attempt = 0; attempt < 50 && requeried is null; attempt++)
        {
            requeried = await callerBoundary.TryGetDepositOutcomeAsync(idempotencyKey);
            if (requeried is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            }
        }

        Assert.IsNotNull(requeried, "Requerying must eventually observe the committed deposit.");
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, requeried!.Kind);
        Assert.AreEqual(biotaId, requeried.Value!.BiotaId);

        var actualResult = await slowDeposit;
        Assert.AreEqual(requeried.Value!.Id, actualResult.Value!.Id);
    }

    [TestMethod]
    public async Task Deposit_OfABiotaPreviouslyWithdrawnFromCloudCustody_CreatesANewCustodyRecord_InsteadOfConflicting()
    {
        // Issue #13 review, finding 2: CloudOwnerIdentity.DepositIdempotencyKey is deterministic in
        // (shardId, biotaId) alone. Once a biota is withdrawn back to world possession it can
        // legitimately be re-deposited, but re-submitting it recomputes the exact same idempotency
        // key as the original deposit. Without voiding that stale record at withdrawal time, the
        // re-deposit finds it, tries to replay it, and throws CloudCustodyConflictException because
        // the original CloudCustodyRecord row no longer exists.
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositKey = CloudOwnerIdentity.DepositIdempotencyKey(ShardId, biotaId);

        var firstDeposit = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), depositKey);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, firstDeposit.Kind, firstDeposit.Reason);

        var withdrawOutcome = await boundary.WithdrawAsync(firstDeposit.Value!.Id, expectedVersion: 1, NextId(), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, withdrawOutcome.Kind, withdrawOutcome.Reason);

        var secondDeposit = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), depositKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, secondDeposit.Kind, secondDeposit.Reason);
        Assert.AreNotEqual(
            firstDeposit.Value!.Id, secondDeposit.Value!.Id,
            "Re-depositing after a withdrawal must create a new Cloud Custody Record, not replay the withdrawn one.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));
    }

    [TestMethod]
    public async Task TryGetDepositOutcome_BeforeAnyDepositRan_ReturnsNull_NotAFalseFailure()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var result = await boundary.TryGetDepositOutcomeAsync(Guid.NewGuid());

        Assert.IsNull(result);
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
