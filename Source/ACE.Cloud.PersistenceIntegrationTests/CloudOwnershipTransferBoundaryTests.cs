using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #21's missing "immediate cloud transfer" adapter
/// (<see cref="CloudOwnershipTransferAuthority"/>): before this issue,
/// <see cref="CloudOwnershipTransferPolicy"/> (issue #7) had no persistence-layer caller at all --
/// <see cref="CloudCustodyRecord.ChangeOwner"/> was reachable only from Vault Absorption's bulk,
/// policy-free reassignment (<see cref="CloudAllegianceVaultGateway.AbsorbAsync"/>), which
/// deliberately skips the per-item reservation precondition because vault contents can never be
/// reserved. These tests exercise the single-target, fully-guarded transfer path: reservation
/// exclusivity, stale versions, stack-record refusal, and one immutable correlated ledger/outbox
/// event pair per commit (EVT-001, EVT-002, ARCH-006). <see cref="PersistenceOwnershipTransferLedgerOutboxAtomicityInvariantSuiteTests"/>,
/// <see cref="PersistenceOwnershipTransferOptimisticConflictInvariantSuiteTests"/>, and
/// <see cref="PersistenceOwnershipTransferIdempotentCommandInvariantSuiteTests"/> cover the generic
/// shared-suite properties (crash-before-commit atomicity, concurrent version conflicts, idempotent
/// replay) so this file only covers what is specific to ownership transfer.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudOwnershipTransferBoundaryTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 790_000;

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
    public async Task Transfer_AReservationFreeWholeItem_ReassignsOwner_BumpsVersion_AndAppendsOneLedgerAndOutboxEvent()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var currentOwnerId = Guid.NewGuid();
        var newOwnerId = Guid.NewGuid();
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var setupContext = new CloudDbContext(options))
        {
            setupContext.CloudCustodyRecords.Add(new CloudCustodyRecord(biotaId, ShardId, currentOwnerId, Guid.NewGuid()));
            await setupContext.SaveChangesAsync();
        }

        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        var outcome = await authority.TransferAsync(biotaId, newOwnerId, expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.AreEqual(newOwnerId, outcome.Value!.OwnerId);
        Assert.AreEqual(2, outcome.Value!.Version);

        await using var verifyContext = new CloudDbContext(options);
        var record = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(newOwnerId, record.OwnerId);
        Assert.AreEqual(2, record.Version);

        var ledgerEvent = await verifyContext.CloudActivityLedgerEvents.AsNoTracking().SingleAsync(e => e.BiotaId == biotaId);
        Assert.AreEqual(CloudBoundaryOperationType.OwnershipTransfer, ledgerEvent.EventType);
        Assert.AreEqual(newOwnerId, ledgerEvent.OwnerId);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, ledgerEvent.Outcome);

        var outboxEvent = await verifyContext.CloudCustodyOutboxEvents.AsNoTracking().SingleAsync(e => e.BiotaId == biotaId);
        Assert.AreEqual(CloudBoundaryOperationType.OwnershipTransfer, outboxEvent.EventType);
        Assert.AreEqual(ledgerEvent.CorrelationId, outboxEvent.CorrelationId);
        Assert.AreEqual(newOwnerId, outboxEvent.OwnerId);
    }

    [TestMethod]
    public async Task Transfer_AWholeItemWithAnActiveWithdrawalReservation_IsRejectedAsAConflict_AndAppendsNoLedgerOrOutboxEvent()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var ownerId = Guid.NewGuid();
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var setupContext = new CloudDbContext(options))
        {
            var custodyBoundary = new CloudCustodyBoundary(setupContext);
            Assert.AreEqual(
                CloudBoundaryOutcomeKind.Committed,
                (await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

            var reserveOutcome = await custodyBoundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
        }

        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        var outcome = await authority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "reserved");

        await using var verifyContext = new CloudDbContext(options);
        var record = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(ownerId, record.OwnerId, "A refused transfer must never change the current owner.");

        Assert.IsFalse(
            await verifyContext.CloudActivityLedgerEvents.AnyAsync(e => e.BiotaId == biotaId && e.EventType == CloudBoundaryOperationType.OwnershipTransfer),
            "A refused transfer must never append an OwnershipTransfer ledger event.");
        Assert.IsFalse(
            await verifyContext.CloudCustodyOutboxEvents.AnyAsync(e => e.BiotaId == biotaId && e.EventType == CloudBoundaryOperationType.OwnershipTransfer),
            "A refused transfer must never append an OwnershipTransfer outbox event.");
    }

    [TestMethod]
    public async Task Transfer_AfterItsBlockingReservationIsCancelled_Succeeds()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var ownerId = Guid.NewGuid();
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        Guid reservationId;
        await using (var setupContext = new CloudDbContext(options))
        {
            var custodyBoundary = new CloudCustodyBoundary(setupContext);
            Assert.AreEqual(
                CloudBoundaryOutcomeKind.Committed,
                (await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

            var reserveOutcome = await custodyBoundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
            reservationId = reserveOutcome.Value!.Id;

            var cancelOutcome = await custodyBoundary.CancelWithdrawalReservationAsync(reservationId, expectedVersion: 1);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, cancelOutcome.Kind, cancelOutcome.Reason);
        }

        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        var outcome = await authority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
    }

    [TestMethod]
    public async Task Transfer_AStackCustodyRecord_IsRejected()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using (var setupContext = new CloudDbContext(options))
        {
            setupContext.CloudCustodyRecords.Add(CloudCustodyRecord.CreateStack(biotaId, ShardId, totalQuantity: 5, Guid.NewGuid()));
            await setupContext.SaveChangesAsync();
        }

        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        var outcome = await authority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "stack");
    }

    [TestMethod]
    public async Task Transfer_ToTheSameOwner_IsRejected()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var ownerId = Guid.NewGuid();
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using (var setupContext = new CloudDbContext(options))
        {
            setupContext.CloudCustodyRecords.Add(new CloudCustodyRecord(biotaId, ShardId, ownerId, Guid.NewGuid()));
            await setupContext.SaveChangesAsync();
        }

        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        var outcome = await authority.TransferAsync(biotaId, ownerId, expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task Transfer_ANonexistentBiota_IsRejectedAsAConflict()
    {
        var biotaId = NextId();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        var outcome = await authority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task TryGetTransferOutcomeAsync_BeforeAnyCommit_ReturnsNull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var authority = new CloudOwnershipTransferAuthority(context);

        Assert.IsNull(await authority.TryGetTransferOutcomeAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task TryGetTransferOutcomeAsync_AfterACommit_ReplaysTheCommittedOwner()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var newOwnerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var setupContext = new CloudDbContext(options))
        {
            setupContext.CloudCustodyRecords.Add(new CloudCustodyRecord(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid()));
            await setupContext.SaveChangesAsync();
        }

        await using (var context = new CloudDbContext(options))
        {
            var authority = new CloudOwnershipTransferAuthority(context);
            var outcome = await authority.TransferAsync(biotaId, newOwnerId, expectedVersion: 1, idempotencyKey);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        }

        await using var verifyContext = new CloudDbContext(options);
        var verifyAuthority = new CloudOwnershipTransferAuthority(verifyContext);
        var replay = await verifyAuthority.TryGetTransferOutcomeAsync(idempotencyKey);

        Assert.IsNotNull(replay);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, replay!.Kind);
        Assert.AreEqual(newOwnerId, replay.Value!.OwnerId);
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private static string NewTokenHash() => Convert.ToHexString(Guid.NewGuid().ToByteArray());
}
