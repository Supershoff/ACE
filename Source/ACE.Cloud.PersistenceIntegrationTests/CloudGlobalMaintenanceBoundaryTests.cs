using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #23's ADM-004 section: "Test Global Cloud Maintenance entry/exit,
/// reason/confirmation, every mutation gate, nested/repeated commands, exact deadline shifting, and
/// commit-time revalidation." Also proves the acceptance criterion "Maintenance never cancels/unlocks
/// assets and shifts clocks by the exact frozen duration."
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudGlobalMaintenanceBoundaryTests
{
    private const string ShardId = "us1";
    private const uint AdminAccessLevel = 5;
    private const uint NonAdminAccessLevel = 1;
    private const uint AdminAccountId = 999;

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

    private CloudGlobalMaintenanceBoundary NewBoundary(out CloudDbContext context)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        context = new CloudDbContext(options);
        return new CloudGlobalMaintenanceBoundary(context);
    }

    [TestMethod]
    public async Task GetCurrent_OnFirstEverRead_BootstrapsAnOpenState()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var state = await boundary.GetCurrentAsync(ShardId);

        Assert.IsFalse(state.IsFrozen);
        Assert.AreEqual(CloudAggregateVersion.Initial, state.Version);
    }

    [TestMethod]
    public async Task Enter_WithReasonAndConfirmationByAnAdmin_Succeeds_AndAppendsAnEnteredLedgerEvent()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var outcome = await boundary.EnterAsync(ShardId, "scheduled downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.IsTrue(outcome.Value!.IsFrozen);

        var ledgerEvent = await context.Set<CloudGlobalMaintenanceLedgerEvent>().AsNoTracking().SingleAsync(e => e.ShardId == ShardId);
        Assert.AreEqual(CloudGlobalMaintenanceLedgerEventType.Entered, ledgerEvent.EventType);
        Assert.AreEqual("scheduled downtime", ledgerEvent.Reason);
        Assert.AreEqual(AdminAccountId, ledgerEvent.ActorAccountId);
    }

    [TestMethod]
    public async Task Enter_WithoutReason_IsRejected_AndCommitsNothing()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var outcome = await boundary.EnterAsync(ShardId, "", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        Assert.HasCount(0, await context.Set<CloudGlobalMaintenanceLedgerEvent>().ToListAsync());
    }

    [TestMethod]
    public async Task Enter_ByANonAdmin_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var outcome = await boundary.EnterAsync(ShardId, "downtime", confirmed: true, NonAdminAccessLevel, AdminAccountId, initial.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task Enter_WhileAlreadyFrozen_IsRejected_ANestedOrRepeatedEntryNeverExtendsOrDuplicatesTheFreeze()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var first = await boundary.EnterAsync(ShardId, "first", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.EnterAsync(ShardId, "second", confirmed: true, AdminAccessLevel, AdminAccountId, first.Value!.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, second.Kind);

        var current = await boundary.GetCurrentAsync(ShardId);
        Assert.AreEqual("first", current.Reason, "A rejected nested Enter must never overwrite the original freeze's reason.");
    }

    [TestMethod]
    public async Task Exit_WhileNotFrozen_IsRejected_ARepeatedExitIsRefusedNotSilentlyAccepted()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var outcome = await boundary.ExitAsync(ShardId, confirmed: true, AdminAccessLevel, initial.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task Exit_ByANonAdmin_IsRejected()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var entered = await boundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);

        var outcome = await boundary.ExitAsync(ShardId, confirmed: true, NonAdminAccessLevel, entered.Value!.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task Exit_AfterEnter_Succeeds_ReopensTheGate_AndAppendsAnExitedLedgerEvent()
    {
        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var entered = await boundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);

        var exited = await boundary.ExitAsync(ShardId, confirmed: true, AdminAccessLevel, entered.Value!.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, exited.Kind);
        Assert.IsFalse(exited.Value!.IsFrozen);

        var exitEvent = await context.Set<CloudGlobalMaintenanceLedgerEvent>().AsNoTracking()
            .SingleAsync(e => e.EventType == CloudGlobalMaintenanceLedgerEventType.Exited);
        Assert.IsNotNull(exitEvent.FrozenDurationSeconds);
    }

    [TestMethod]
    public async Task Exit_ShiftsEveryOpenWithdrawalReservationsExpiry_ByExactlyTheLedgeredFrozenDuration_NeverCancellingIt()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        var reserveOutcome = await custodyBoundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId,
            Convert.ToHexString(Guid.NewGuid().ToByteArray()), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind);
        var expiresBeforeFreeze = reserveOutcome.Value!.ExpiresAtUtc;

        var boundary = NewBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var entered = await boundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, entered.Kind);

        // Simulate real time having passed while frozen: back-date the persisted EnteredAtUtc so the
        // exit below computes a real, non-zero frozen duration deterministically, without a test
        // sleep.
        await BackdateEnteredAtUtcAsync(TimeSpan.FromMinutes(37));

        var exited = await boundary.ExitAsync(ShardId, confirmed: true, AdminAccessLevel, entered.Value!.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, exited.Kind);

        var exitEvent = await context.Set<CloudGlobalMaintenanceLedgerEvent>().AsNoTracking()
            .SingleAsync(e => e.EventType == CloudGlobalMaintenanceLedgerEventType.Exited);
        var frozenDuration = TimeSpan.FromSeconds(exitEvent.FrozenDurationSeconds!.Value);

        var reservationAfterExit = await context.Set<CloudWithdrawalReservation>().AsNoTracking().SingleAsync(r => r.Id == reserveOutcome.Value.Id);

        Assert.AreEqual(
            expiresBeforeFreeze.Add(frozenDuration), reservationAfterExit.ExpiresAtUtc,
            "ADM-004: resume must shift the reservation's deadline by exactly the frozen duration.");
        Assert.AreEqual(
            CloudReservationStatus.Active, reservationAfterExit.Status,
            "ADM-004: maintenance must never cancel or unlock an in-flight Withdrawal Reservation.");
    }

    [TestMethod]
    public async Task WhileFrozen_ReserveForWithdrawal_IsRefused_ProvingTheRealGateBlocksTheReservationOpenCallSite()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using var _ = maintenanceContext;
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var reserveOutcome = await custodyBoundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId,
            Convert.ToHexString(Guid.NewGuid().ToByteArray()), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, reserveOutcome.Kind);
        StringAssert.Contains(reserveOutcome.Reason, "frozen");
    }

    [TestMethod]
    public async Task WhileFrozen_OwnershipTransfer_IsRefused_ProvingTheRealGateBlocksTheTransferAuthority()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using var _ = maintenanceContext;
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var transferAuthority = new CloudOwnershipTransferAuthority(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var transferOutcome = await transferAuthority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, transferOutcome.Kind);
        StringAssert.Contains(transferOutcome.Reason, "frozen");
    }

    [TestMethod]
    public async Task WhileFrozen_Deposit_IsRefused_ProvingTheRealGateBlocksTheDepositCallSite()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using var _ = maintenanceContext;
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var depositOutcome = await custodyBoundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, depositOutcome.Kind);
        StringAssert.Contains(depositOutcome.Reason, "frozen");
        Assert.IsTrue(
            await AceShardTestData.BiotaExistsAsync(_fixture.AceShardConnectionString, biotaId),
            "A refused deposit must never remove the biota from the world.");
    }

    [TestMethod]
    public async Task WhileFrozen_Withdraw_IsRefused_ProvingTheRealGateBlocksTheWithdrawCallSite()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        var depositOutcome = await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using var _ = maintenanceContext;
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var withdrawOutcome = await custodyBoundary.WithdrawAsync(depositOutcome.Value!.Id, expectedVersion: 1, NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, withdrawOutcome.Kind);
        StringAssert.Contains(withdrawOutcome.Reason, "frozen");
    }

    [TestMethod]
    public async Task WhileFrozen_DepositStack_IsRefused_ProvingTheRealGateBlocksTheStackDepositCallSite()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using var _ = maintenanceContext;
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var depositOutcome = await custodyBoundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 10, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, depositOutcome.Kind);
        StringAssert.Contains(depositOutcome.Reason, "frozen");
    }

    [TestMethod]
    public async Task WhileFrozen_WithdrawLot_IsRefused_ProvingTheRealGateBlocksTheLotWithdrawalCallSite()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var depositOutcome = await custodyBoundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 10, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
        var lot = depositOutcome.Value!.Lot;

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using var _ = maintenanceContext;
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var withdrawOutcome = await custodyBoundary.WithdrawLotAsync(lot.Id, lot.Version, 10, NextId(), materializedBiotaId: null, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, withdrawOutcome.Kind);
        StringAssert.Contains(withdrawOutcome.Reason, "frozen");
    }

    /// <summary>
    /// Red -&gt; Green regression test for issue #23's review [P0]: proves the mutation gate survives a
    /// Global Cloud Maintenance entry that commits <em>concurrently</em> with an in-flight lot
    /// withdrawal, not only one that enters completely before it starts (unlike
    /// <see cref="WhileFrozen_WithdrawLot_IsRefused_ProvingTheRealGateBlocksTheLotWithdrawalCallSite"/>,
    /// which never exercises the timing window). Pauses the withdrawal at
    /// <see cref="CloudBoundaryFaultPoint.AfterLocks"/> -- once its row locks are held but before its
    /// first plain (non-locking) read -- lets a second transaction enter and commit Global Cloud
    /// Maintenance, then resumes the withdrawal. Under MariaDB's REPEATABLE READ, a transaction whose
    /// first query is a plain read fixes its whole consistent-read snapshot at that read; if the
    /// withdrawal's mutation-gate check ever reused a snapshot fixed before this window, this
    /// maintenance entry would go unobserved and the withdrawal would wrongly commit.
    /// </summary>
    [TestMethod]
    public async Task WhileFrozen_WithdrawLot_ConcurrentMaintenanceEntry_IsRefused_NotJustASequentialOne()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var depositOutcome = await custodyBoundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 10, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
        var lot = depositOutcome.Value!.Lot;

        var locksAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenanceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CloudBoundaryFaultPoint, Task> pauseAfterLocks = async point =>
        {
            if (point == CloudBoundaryFaultPoint.AfterLocks)
            {
                locksAcquired.TrySetResult();
                await maintenanceEntered.Task;
            }
        };

        var withdrawTask = custodyBoundary.WithdrawLotAsync(
            lot.Id, lot.Version, 10, NextId(), materializedBiotaId: null, Guid.NewGuid(), pauseAfterLocks, CancellationToken.None);

        await locksAcquired.Task;

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using (maintenanceContext)
        {
            var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
            var entered = await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, entered.Kind);
        }

        maintenanceEntered.TrySetResult();

        var withdrawOutcome = await withdrawTask;

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Conflict, withdrawOutcome.Kind,
            "A Global Cloud Maintenance entry that commits after this withdrawal's row locks were acquired -- but before its mutation-gate "
                + "check -- must still be observed, not missed because of a REPEATABLE READ snapshot fixed earlier in the transaction.");
        StringAssert.Contains(withdrawOutcome.Reason, "frozen");
    }

    /// <summary>
    /// Red -&gt; Green regression test for the independent review's [P0] on PR #135: proves the mutation
    /// gate survives a Global Cloud Maintenance entry that commits <em>concurrently</em> with an
    /// in-flight Withdrawal Reservation open whose only target is a Cloud Stack Lot (not a whole
    /// item), unlike <see cref="WhileFrozen_ReserveForWithdrawal_IsRefused_ProvingTheRealGateBlocksTheReservationOpenCallSite"/>,
    /// which only ever reserves an Item target and enters maintenance strictly before starting the
    /// reservation. <see cref="CloudReservationTargetOrdering.LockKey"/> sorts Item targets before
    /// StackLot targets, so a StackLot-only request makes the StackLot branch's lot lookup this
    /// transaction's very first query; if that lookup is a plain (non-locking) read, MariaDB's
    /// REPEATABLE READ fixes the whole transaction's consistent-read snapshot there, before any lock
    /// is taken, and the mutation-gate read later in the same transaction would never observe a
    /// Global Cloud Maintenance entry that commits after this snapshot but before the gate check.
    /// </summary>
    [TestMethod]
    public async Task WhileFrozen_ReserveForWithdrawal_StackLotConcurrentMaintenanceEntry_IsRefused_NotJustASequentialOne()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        var depositOutcome = await custodyBoundary.DepositStackAsync(biotaId, ShardId, ownerId, 10, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);
        var lot = depositOutcome.Value!.Lot;

        var locksAcquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenanceEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Func<CloudBoundaryFaultPoint, Task> pauseAfterLocks = async point =>
        {
            if (point == CloudBoundaryFaultPoint.AfterLocks)
            {
                locksAcquired.TrySetResult();
                await maintenanceEntered.Task;
            }
        };

        var reserveTask = custodyBoundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId,
            Convert.ToHexString(Guid.NewGuid().ToByteArray()), TimeSpan.FromMinutes(15), Guid.NewGuid(), pauseAfterLocks, CancellationToken.None);

        await locksAcquired.Task;

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using (maintenanceContext)
        {
            var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
            var entered = await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, entered.Kind);
        }

        maintenanceEntered.TrySetResult();

        var reserveOutcome = await reserveTask;

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Conflict, reserveOutcome.Kind,
            "A Global Cloud Maintenance entry that commits after this Cloud-Stack-Lot-only reservation's row locks were acquired -- but "
                + "before its mutation-gate check -- must still be observed, not missed because of a REPEATABLE READ snapshot fixed by an "
                + "earlier plain read in the same transaction.");
        StringAssert.Contains(reserveOutcome.Reason, "frozen");
    }

    [TestMethod]
    public async Task WhileFrozen_ConvertPyrealDeposit_IsRefused_ProvingTheRealGateBlocksThePyrealConversionCallSite()
    {
        var rawBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, rawBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, rawBiotaId, 100_000);

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using var _ = maintenanceContext;
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var conversionOutcome = await custodyBoundary.ConvertPyrealDepositAsync(
            rawBiotaId, ShardId, Guid.NewGuid(), 100_000, mmdBiotaIds: [], Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, conversionOutcome.Kind);
        StringAssert.Contains(conversionOutcome.Reason, "frozen");
        Assert.IsTrue(
            await AceShardTestData.BiotaExistsAsync(_fixture.AceShardConnectionString, rawBiotaId),
            "A refused conversion must never consume the raw Pyreal biota.");
    }

    /// <summary>
    /// Red -&gt; Green regression test for issue #23's review [P1]: this PR's own commit message names
    /// the Pyreal Remainder withdrawal open path among the six call sites it wired to the real
    /// mutation gate, but no <c>WhileFrozen_*</c> test proved it, unlike every sibling call site in
    /// this file.
    /// </summary>
    [TestMethod]
    public async Task WhileFrozen_WithdrawPyrealRemainder_IsRefused_ProvingTheRealGateBlocksTheRemainderWithdrawalCallSite()
    {
        var rawBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, rawBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, rawBiotaId, 500);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        var conversionOutcome = await custodyBoundary.ConvertPyrealDepositAsync(rawBiotaId, ShardId, ownerId, 500, mmdBiotaIds: [], Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, conversionOutcome.Kind);

        var deliveryBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, deliveryBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, deliveryBiotaId, 500);

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using var _ = maintenanceContext;
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var withdrawOutcome = await custodyBoundary.WithdrawPyrealRemainderAsync(ShardId, ownerId, 500, [deliveryBiotaId], NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, withdrawOutcome.Kind);
        StringAssert.Contains(withdrawOutcome.Reason, "frozen");

        await using var verifyContext = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var remainder = await verifyContext.CloudPyrealRemainders.AsNoTracking().SingleAsync(r => r.OwnerId == ownerId);
        Assert.AreEqual(500, remainder.RemainderAmount, "A refused withdrawal must leave the remainder exactly unchanged.");
    }

    [TestMethod]
    public async Task WhileFrozen_RedeemWithdrawalReservation_IsRefused_ProvingTheRealGateBlocksTheRedemptionCallSite()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        var tokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var reserveOutcome = await custodyBoundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind);

        var maintenanceBoundary = NewBoundary(out var maintenanceContext);
        await using var _ = maintenanceContext;
        var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);

        var redeemOutcome = await custodyBoundary.RedeemWithdrawalReservationAsync(tokenHash, NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, redeemOutcome.Kind);
        StringAssert.Contains(redeemOutcome.Reason, "frozen");
    }

    [TestMethod]
    public async Task Enter_PersistsAcrossASimulatedRestart()
    {
        await using (var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var boundary = new CloudGlobalMaintenanceBoundary(context);
            var initial = await boundary.GetCurrentAsync(ShardId);
            var outcome = await boundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        }

        await using var restarted = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var restartedBoundary = new CloudGlobalMaintenanceBoundary(restarted);
        var state = await restartedBoundary.GetCurrentAsync(ShardId);

        Assert.IsTrue(state.IsFrozen, "ADM-004: a freeze must persist while ACE is down, exactly like Custodian configuration (DEP-008).");
    }

    private async Task BackdateEnteredAtUtcAsync(TimeSpan by)
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CloudGlobalMaintenance SET EnteredAtUtc = DATE_SUB(EnteredAtUtc, INTERVAL @seconds SECOND) WHERE Id = 1;";
        command.Parameters.AddWithValue("@seconds", by.TotalSeconds);
        await command.ExecuteNonQueryAsync();
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
