using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #17's Green requirement: "Build a disposable ACE/MariaDB phase acceptance fixture covering
/// Custodian deposit through withdrawal," and the linked acceptance criterion "A phase-gate report
/// links passing boundary, race, crash, and restart evidence." This file is that fixture: one
/// disposable MariaDB instance (<see cref="CloudDatabaseFixture"/>, the same ACE/MariaDB harness
/// every other Cloud persistence suite uses) drives a Custodian deposit all the way through a
/// Withdrawal Token reservation and redemption, and each test method below is named for exactly the
/// evidence category the acceptance criterion asks for:
///
///   - Boundary evidence: <see cref="FullLifecycle_DepositReserveAndRedeem_CommitsEveryStageWithOrderedOutboxEvidence"/>
///   - Crash evidence: <see cref="CrashDuringDeposit_ThenIdempotentRetry_RecoversAndTheLifecycleContinuesToRedemption"/>
///   - Race evidence: <see cref="ConcurrentWithdrawalReservationAttempts_OnTheSameBiota_ExactlyOneSucceeds"/>
///   - Restart evidence: <see cref="OutboxEvents_RemainFullyReplayable_ThroughABrandNewContext_AfterASimulatedRestart"/>
///   - DB-down evidence: <see cref="ReserveForWithdrawalAsync_AgainstAnUnreachableDatabase_RefusesWithoutCommittingAnything"/>
///
/// (World-down/web-up and web-down/world-up behavior (ARCH-008, WDR-008) are proved separately by
/// <see cref="CloudGatewayAvailabilityTests"/> and <see cref="CloudCustodyBoundaryWithdrawalTests"/>'s
/// already-issued-token-remains-redeemable coverage; nothing here duplicates that, it is cited as
/// part of the same phase-gate evidence set.)
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudPhaseGateAcceptanceTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 950_000;

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
    public async Task FullLifecycle_DepositReserveAndRedeem_CommitsEveryStageWithOrderedOutboxEvidence()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextId();
        var recipientContainerId = NextId();
        var ownerId = Guid.NewGuid();
        var tokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        // Stage 1: a Cloud Custodian deposit (DEP-001..DEP-002) removes world possession and creates
        // custody atomically.
        var depositOutcome = await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind, depositOutcome.Reason);
        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));

        // Stage 2: a Withdrawal Token's exclusive reservation (WDR-001).
        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)],
            ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        // Stage 3: redemption performs the same custody-to-world transition as an ordinary withdrawal
        // and releases the reservation as fulfilled, atomically.
        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, biotaId, recipientContainerId));

        Assert.AreEqual(0, await context.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId), "Custody must be fully released after redemption.");

        // Boundary evidence: every stage left its own ledger and outbox trail, in commit order.
        var outboxEvents = (await context.CloudCustodyOutboxEvents.AsNoTracking()
            .Where(e => e.BiotaId == biotaId)
            .OrderBy(e => e.SequenceNumber)
            .ToListAsync())
            .Select(e => e.EventType)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                CloudBoundaryOperationType.Deposit,
                CloudBoundaryOperationType.WithdrawalReservationOpened,
                // The actual custody-to-world transition redemption performs is recorded as an
                // ordinary Withdrawal (matching CloudCustodyBoundary.TryRedeemWithdrawalReservationOnceAsync);
                // WithdrawalReservationRedeemed is used only for the idempotency record, not this outbox trail.
                CloudBoundaryOperationType.Withdrawal,
            },
            outboxEvents,
            "The full deposit-through-withdrawal lifecycle must leave exactly this ordered outbox trail.");
    }

    [TestMethod]
    public async Task CrashDuringDeposit_ThenIdempotentRetry_RecoversAndTheLifecycleContinuesToRedemption()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextId();
        var recipientContainerId = NextId();
        var ownerId = Guid.NewGuid();
        var depositIdempotencyKey = Guid.NewGuid();
        var tokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.GrantContainerAsync(_fixture.AceShardConnectionString, biotaId, containerId: NextId());

        await using var crashingContext = new CloudDbContext(options);
        var crashingBoundary = new CloudCustodyBoundary(crashingContext);

        Func<CloudBoundaryFaultPoint, Task> crashAfterCustodyChange = point =>
            point == CloudBoundaryFaultPoint.AfterCustodyChange
                ? throw new CloudBoundarySimulatedCrashException(point)
                : Task.CompletedTask;

        // Crash evidence: a simulated process crash exactly after the custody-record insert, before
        // the ledger/outbox/commit that would make it durable.
        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(
            () => crashingBoundary.DepositAsync(biotaId, ShardId, ownerId, depositIdempotencyKey, crashAfterCustodyChange, CancellationToken.None));

        await using (var verifyContext = new CloudDbContext(options))
        {
            Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId), "The crashed attempt must have rolled back completely.");
        }
        Assert.IsTrue(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId), "World possession must survive a crashed deposit attempt untouched.");

        // A "restarted caller" retries with the same idempotency key and actually commits this time.
        await using var retryContext = new CloudDbContext(options);
        var retryBoundary = new CloudCustodyBoundary(retryContext);
        var depositOutcome = await retryBoundary.DepositAsync(biotaId, ShardId, ownerId, depositIdempotencyKey);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind, depositOutcome.Reason);

        // The lifecycle continues normally from here: recovery from a crash is not a dead end.
        var reserveOutcome = await retryBoundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)],
            ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var redeemOutcome = await retryBoundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
    }

    [TestMethod]
    public async Task ConcurrentWithdrawalReservationAttempts_OnTheSameBiota_ExactlyOneSucceeds()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextId();
        var ownerId = Guid.NewGuid();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var seedContext = new CloudDbContext(options))
        {
            var seedBoundary = new CloudCustodyBoundary(seedContext);
            var depositOutcome = await seedBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
        }

        // Race evidence: several concurrent attempts to open a Withdrawal Reservation for the same
        // biota (INV-001: "One quantity may have at most one exclusive reservation").
        var tasks = Enumerable.Range(0, 6).Select(async i =>
        {
            await using var context = new CloudDbContext(options);
            var boundary = new CloudCustodyBoundary(context);
            var tokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray());
            return await boundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)],
                ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        });

        var results = await Task.WhenAll(tasks);

        Assert.HasCount(1, results.Where(r => r.Kind == CloudBoundaryOutcomeKind.Committed), "Exactly one concurrent reservation attempt may win.");
        Assert.HasCount(5, results.Where(r => r.Kind == CloudBoundaryOutcomeKind.Conflict), "Every other concurrent attempt must observe the exclusivity conflict, not silently double-reserve.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudWithdrawalReservationTargets.CountAsync(t => t.ItemBiotaId == biotaId), "Only one reservation target row may exist for this biota.");
    }

    [TestMethod]
    public async Task OutboxEvents_RemainFullyReplayable_ThroughABrandNewContext_AfterASimulatedRestart()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var biotaId = NextId();
        var ownerId = Guid.NewGuid();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
        }

        // Restart evidence: nothing in this process kept the boundary/context alive between the
        // deposit above and this read -- a brand-new CloudDbContext/reader, exactly what a companion
        // process would construct after restarting, must still see it (ARCH-007).
        await using var restartedContext = new CloudDbContext(options);
        var reader = new CloudCustodyOutboxReader(restartedContext);
        var events = await reader.ReadAfterAsync(afterSequenceNumber: 0, maxCount: 100);

        Assert.HasCount(1, events);
        Assert.AreEqual(biotaId, events[0].BiotaId);
        Assert.AreEqual(CloudBoundaryOperationType.Deposit, events[0].EventType);
    }

    [TestMethod]
    public async Task ReserveForWithdrawalAsync_AgainstAnUnreachableDatabase_RefusesWithoutCommittingAnything()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var reachableOptions = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var serverVersion = await Task.Run(() => ServerVersion.AutoDetect(_fixture.CloudConnectionString));

        await using (var seedContext = new CloudDbContext(reachableOptions))
        {
            var seedBoundary = new CloudCustodyBoundary(seedContext);
            await seedBoundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        }

        var unreachableBuilder = new MySqlConnectionStringBuilder(_fixture.CloudConnectionString)
        {
            Server = "127.0.0.1",
            Port = 1,
            ConnectionTimeout = 2,
        };
        var unreachableOptions = CloudDbContextOptionsFactory.Create(unreachableBuilder.ConnectionString, serverVersion);

        // DB-down evidence (ARCH-009/WDR-008): a caller that cannot reach MariaDB at all gets an
        // explicit typed refusal, never queues the mutation for later replay.
        await using var unreachableContext = new CloudDbContext(unreachableOptions);
        var unreachableBoundary = new CloudCustodyBoundary(unreachableContext);
        var outcome = await unreachableBoundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)],
            ShardId, Guid.NewGuid(), Convert.ToHexString(Guid.NewGuid().ToByteArray()), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Unavailable, outcome.Kind);

        await using var verifyContext = new CloudDbContext(reachableOptions);
        Assert.AreEqual(0, await verifyContext.CloudWithdrawalReservationTargets.CountAsync(t => t.ItemBiotaId == biotaId), "A refused reservation attempt must commit nothing, ever.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
