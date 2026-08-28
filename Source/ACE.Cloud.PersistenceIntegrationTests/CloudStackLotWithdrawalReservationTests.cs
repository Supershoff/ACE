using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #16's Cloud Stack Lot withdrawal reservation (WDR-001, WDR-002,
/// WDR-003, WDR-008, INV-002, INV-003): reserving, redeeming, and cancelling a Withdrawal Token whose
/// selection is a quantity claim against a stackable biota rather than a whole item, including
/// ACE-only materialization of the delivered child biota when the reserved lot is not the sole lot
/// backing its stack.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStackLotWithdrawalReservationTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 1_100_000;

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
    public async Task ReserveStackLot_ThenRedeem_OfTheOnlyLot_DeliversTheOriginalBiota_ReleasesTheReservationAsFulfilled()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 25, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var reserveOutcome = await boundary.ReserveStackLotForWithdrawalAsync(
            lot.Id, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
        Assert.AreEqual(CloudReservationStatus.Active, reserveOutcome.Value!.Status);
        Assert.AreEqual(25, reserveOutcome.Value!.Quantity);

        var redeemOutcome = await boundary.RedeemStackLotWithdrawalReservationAsync(
            tokenHash, recipientContainerId, materializedBiotaId: null, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
        Assert.AreEqual(biotaId, redeemOutcome.Value!.DeliveredBiotaId, "A full-lot withdrawal of the only lot must deliver the original biota GUID.");
        Assert.AreEqual(25, redeemOutcome.Value!.Quantity);
        Assert.AreEqual(ownerId, redeemOutcome.Value!.FormerOwnerId);

        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, biotaId, recipientContainerId));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudStackLots.CountAsync(l => l.Id == lot.Id));
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.Id == depositOutcome.Value!.CustodyRecord.Id));

        var reservation = await verifyContext.CloudStackLotWithdrawalReservations.AsNoTracking().SingleAsync(r => r.LotId == lot.Id);
        Assert.AreEqual(CloudReservationStatus.Released, reservation.Status);
        Assert.AreEqual(CloudReservationReleaseReason.Fulfilled, reservation.ReleaseReason);

        Assert.IsNull(await boundary.TryGetActiveStackLotWithdrawalReservationAsync(tokenHash), "A fulfilled reservation is no longer active.");
    }

    [TestMethod]
    public async Task ReserveStackLot_ThenRedeem_OfAPartialLot_MaterializesAChildBiota_AndPreservesTheOriginalGuidWithTheRemainder()
    {
        var originalBiotaId = NextId();
        var materializedBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, originalBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        // A reservation always covers one whole lot (the same exclusivity granularity
        // CloudReservationTarget.ForStackLot already models), so exercising partial materialization
        // under a reservation requires the reserved lot not to be the sole lot on its custody record.
        // Splitting the lot is a Cloud-only operation (docs/adr/0002): CloudStackLotTransactionAuthority
        // carves a second lot off, leaving the original GUID with the remainder.
        var depositOutcome = await boundary.DepositStackAsync(originalBiotaId, ShardId, ownerId, 100, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var lotAuthority = new CloudStackLotTransactionAuthority(context);
        var splitOutcome = await lotAuthority.SplitLotAsync(lot.Id, lot.Version, ownerId, quantityToSplit: 40);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, splitOutcome.Kind, splitOutcome.Reason);
        var splitLotId = splitOutcome.Value!.NewLot.Id;

        var reserveOutcome = await boundary.ReserveStackLotForWithdrawalAsync(
            splitLotId, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
        Assert.AreEqual(40, reserveOutcome.Value!.Quantity);

        var redeemOutcome = await boundary.RedeemStackLotWithdrawalReservationAsync(
            tokenHash, recipientContainerId, materializedBiotaId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
        Assert.AreEqual(materializedBiotaId, redeemOutcome.Value!.DeliveredBiotaId, "A partial-lot redemption must deliver the materialized child GUID, not the original.");
        Assert.AreEqual(40, redeemOutcome.Value!.Quantity);

        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, materializedBiotaId, recipientContainerId));
        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, originalBiotaId), "The original biota's remaining quantity stays in Cloud custody, not world possession.");

        await using var verifyContext = new CloudDbContext(options);
        var remainingRecord = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.Id == depositOutcome.Value!.CustodyRecord.Id);
        Assert.AreEqual(60, remainingRecord.TotalQuantity, "100 deposited minus the 40 redeemed must leave exactly 60 in Cloud custody.");

        var lineage = await verifyContext.CloudStackLotLineageEvents.AsNoTracking().SingleAsync(e => e.ChildBiotaId == materializedBiotaId);
        Assert.AreEqual(originalBiotaId, lineage.ParentBiotaId);
        Assert.AreEqual(40, lineage.Quantity);
    }

    [TestMethod]
    public async Task ReserveStackLot_WhileAnActiveReservationAlreadyExistsForTheSameLot_ReturnsConflict()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 10, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var first = await boundary.ReserveStackLotForWithdrawalAsync(lot.Id, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.ReserveStackLotForWithdrawalAsync(lot.Id, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, second.Kind);
    }

    [TestMethod]
    public async Task ReserveStackLot_ForALotNotOwnedByTheCaller_ReturnsConflict()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 10, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var outcome = await boundary.ReserveStackLotForWithdrawalAsync(
            lot.Id, ShardId, Guid.NewGuid(), NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task ReserveStackLot_RepeatedIdempotencyKey_ReplaysTheOriginalReservation_WithoutOpeningASecondOne()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();
        var tokenHash = NewTokenHash();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 10, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var first = await boundary.ReserveStackLotForWithdrawalAsync(lot.Id, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), idempotencyKey);
        var second = await boundary.ReserveStackLotForWithdrawalAsync(lot.Id, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.Id, second.Value!.Id);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudStackLotWithdrawalReservations.CountAsync(r => r.LotId == lot.Id));
    }

    [TestMethod]
    public async Task Redeem_AnExpiredStackLotReservation_ReturnsConflict_AndLeavesTheLotAndCustodyUntouched()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 10, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var reserveOutcome = await boundary.ReserveStackLotForWithdrawalAsync(
            lot.Id, ShardId, ownerId, tokenHash, TimeSpan.FromTicks(1), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var redeemOutcome = await boundary.RedeemStackLotWithdrawalReservationAsync(tokenHash, NextId(), materializedBiotaId: null, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, redeemOutcome.Kind);
        StringAssert.Contains(redeemOutcome.Reason, "expired");

        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(10, await verifyContext.CloudStackLots.Where(l => l.Id == lot.Id).Select(l => l.Quantity).SingleAsync());
    }

    [TestMethod]
    public async Task Cancel_AnActiveStackLotReservation_ReleasesIt_AndFreesTheLotForANewReservation()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 10, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var reserveOutcome = await boundary.ReserveStackLotForWithdrawalAsync(
            lot.Id, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        var reservationId = reserveOutcome.Value!.Id;

        var cancelOutcome = await boundary.CancelStackLotWithdrawalReservationAsync(reservationId, expectedVersion: 1);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, cancelOutcome.Kind);
        Assert.AreEqual(CloudReservationStatus.Released, cancelOutcome.Value!.Status);

        var reopenOutcome = await boundary.ReserveStackLotForWithdrawalAsync(
            lot.Id, ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reopenOutcome.Kind, "Cancelling must free the lot for a fresh reservation.");
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
        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 10, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;
        await boundary.ReserveStackLotForWithdrawalAsync(lot.Id, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());

        var first = await boundary.RedeemStackLotWithdrawalReservationAsync(tokenHash, recipientContainerId, materializedBiotaId: null, idempotencyKey);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.RedeemStackLotWithdrawalReservationAsync(tokenHash, recipientContainerId, materializedBiotaId: null, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.DeliveredBiotaId, second.Value!.DeliveredBiotaId);

        var containerRowCount = await AceShardTestData.CountContainerRowsAsync(_fixture.AceShardConnectionString, biotaId);
        Assert.AreEqual(1, containerRowCount, "Replaying a committed redemption must not grant world possession a second time.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private static string NewTokenHash() => Convert.ToHexString(Guid.NewGuid().ToByteArray());
}
