using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #11's local withdrawal authority record (WDR-001, WDR-002, WDR-003,
/// WDR-008): opening, redeeming, and cancelling a Withdrawal Reservation entirely from ACE's own
/// database, and the exclusivity/expiry/idempotency rules that make an already-issued Withdrawal
/// Token safely redeemable without the companion web service.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudWithdrawalReservationTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 700_000;

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
    public async Task ReserveForWithdrawal_ThenRedeem_DeliversTheItem_ReleasesTheReservationAsFulfilled_AndAppendsLedgerAndOutbox()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind);

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            biotaId, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind);
        Assert.AreEqual(CloudReservationStatus.Active, reserveOutcome.Value!.Status);

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind);
        Assert.AreEqual(biotaId, redeemOutcome.Value!.BiotaId);
        Assert.AreEqual(recipientContainerId, redeemOutcome.Value!.RecipientContainerId);
        Assert.AreEqual(ownerId, redeemOutcome.Value!.FormerOwnerId);

        Assert.IsTrue(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));

        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(CloudReservationStatus.Released, reservation.Status);
        Assert.AreEqual(CloudReservationReleaseReason.Fulfilled, reservation.ReleaseReason);

        var ledgerEvents = await verifyContext.CloudActivityLedgerEvents.Where(e => e.BiotaId == biotaId).ToListAsync();
        Assert.HasCount(3, ledgerEvents, "Deposit, ReservationOpened, and Withdrawal (redemption) must each append one ledger event.");

        var outboxEvents = await verifyContext.CloudCustodyOutboxEvents.Where(e => e.BiotaId == biotaId).ToListAsync();
        Assert.HasCount(3, outboxEvents);

        Assert.IsNull(await boundary.TryGetActiveWithdrawalReservationAsync(tokenHash), "A fulfilled reservation is no longer active.");
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_WhileAnActiveReservationAlreadyExistsForTheSameBiota_ReturnsConflict()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
        var first = await boundary.ReserveForWithdrawalAsync(biotaId, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.ReserveForWithdrawalAsync(biotaId, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, second.Kind);
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_RepeatedIdempotencyKey_ReplaysTheOriginalReservation_WithoutOpeningASecondOne()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var tokenHash = NewTokenHash();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());

        var first = await boundary.ReserveForWithdrawalAsync(biotaId, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), idempotencyKey);
        var second = await boundary.ReserveForWithdrawalAsync(biotaId, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.Id, second.Value!.Id);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudWithdrawalReservations.CountAsync(r => r.BiotaId == biotaId));
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_ForAnItemNotOwnedByTheCaller_ReturnsConflict()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        var outcome = await boundary.ReserveForWithdrawalAsync(
            biotaId, ShardId, Guid.NewGuid(), NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_ForAStackCustodyRecord_ReturnsConflict()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        await boundary.DepositStackAsync(biotaId, ShardId, ownerId, quantity: 10, Guid.NewGuid());

        var outcome = await boundary.ReserveForWithdrawalAsync(
            biotaId, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "stack");
    }

    [TestMethod]
    public async Task Redeem_AnExpiredReservation_ReturnsConflict_AndLeavesCustodyAndTheReservationUntouched()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());

        // A time-to-live of one tick is always already expired by the time redemption reads the
        // database clock, without depending on wall-clock sleeps in the test.
        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            biotaId, ShardId, ownerId, tokenHash, TimeSpan.FromTicks(1), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, redeemOutcome.Kind);
        StringAssert.Contains(redeemOutcome.Reason, "expired");

        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));

        await using var verifyContext = new CloudDbContext(options);
        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(CloudReservationStatus.Active, reservation.Status, "Expiry alone never releases a reservation; only an explicit release does.");
    }

    [TestMethod]
    public async Task Redeem_UnknownTokenHash_ReturnsConflict_WithoutThrowing()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.RedeemWithdrawalReservationAsync(NewTokenHash(), NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task Redeem_RepeatedIdempotencyKey_ReplaysTheCommittedResult_WithoutReapplyingTheGrant()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();
        var idempotencyKey = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
        await boundary.ReserveForWithdrawalAsync(biotaId, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());

        var first = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, idempotencyKey);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.BiotaId, second.Value!.BiotaId);

        var containerRowCount = await AceShardTestData.CountContainerRowsAsync(_fixture.AceShardConnectionString, biotaId);
        Assert.AreEqual(1, containerRowCount, "Replaying a committed redemption must not grant world possession a second time.");
    }

    [TestMethod]
    public async Task Cancel_AnActiveReservation_ReleasesIt_AndFreesTheBiotaForANewReservation()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            biotaId, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        var reservationId = reserveOutcome.Value!.Id;

        var cancelOutcome = await boundary.CancelWithdrawalReservationAsync(reservationId, expectedVersion: 1);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, cancelOutcome.Kind);
        Assert.AreEqual(CloudReservationStatus.Released, cancelOutcome.Value!.Status);
        Assert.AreEqual(CloudReservationReleaseReason.Cancelled, cancelOutcome.Value!.ReleaseReason);

        var reopenOutcome = await boundary.ReserveForWithdrawalAsync(
            biotaId, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reopenOutcome.Kind, "Cancelling must free the biota for a fresh reservation.");
    }

    [TestMethod]
    public async Task Cancel_AnAlreadyCancelledReservation_IsIdempotent_AndReturnsCommitted()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            biotaId, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        var reservationId = reserveOutcome.Value!.Id;

        var first = await boundary.CancelWithdrawalReservationAsync(reservationId, expectedVersion: 1);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.CancelWithdrawalReservationAsync(reservationId, expectedVersion: 1);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind, "Cancelling an already-cancelled reservation is a no-op success.");
    }

    [TestMethod]
    public async Task Cancel_AnAlreadyRedeemedReservation_ReturnsConflict()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            biotaId, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        var reservationId = reserveOutcome.Value!.Id;

        await boundary.RedeemWithdrawalReservationAsync(tokenHash, NextId(), Guid.NewGuid());

        var cancelOutcome = await boundary.CancelWithdrawalReservationAsync(reservationId, expectedVersion: 1);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, cancelOutcome.Kind);
        StringAssert.Contains(cancelOutcome.Reason, "Fulfilled");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private static string NewTokenHash() => Convert.ToHexString(Guid.NewGuid().ToByteArray());
}
