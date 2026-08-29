using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Focused Cloud Stack Lot coverage for issue #122's unified Withdrawal Reservation: single-lot
/// reservation lifecycle through the merged <see cref="CloudCustodyBoundary.ReserveForWithdrawalAsync(System.Collections.Generic.IReadOnlyList{CloudWithdrawalReservationRequestTarget}, string, Guid, string, TimeSpan, Guid, System.Threading.CancellationToken)"/>
/// API (mirroring what a standalone <c>CloudStackLotWithdrawalReservation</c> table proved before
/// this issue merged it away), the Cloud Stack Lot Transaction Authority's interlock against a lot
/// with an active reservation, and the redemption-time quantity-drift guard. Mixed multi-target
/// coverage lives in <see cref="CloudWithdrawalReservationTests"/>; crash-at-every-commit-boundary
/// evidence lives in <see cref="CloudWithdrawalReservationFaultInjectionTests"/>.
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

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
        Assert.AreEqual(CloudReservationStatus.Active, reserveOutcome.Value!.Status);

        var targets = await boundary.GetReservationTargetsAsync(reserveOutcome.Value!.Id);
        Assert.AreEqual(25, targets.Single().Quantity);

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
        Assert.HasCount(1, redeemOutcome.Value!.Deliveries);
        Assert.AreEqual(biotaId, redeemOutcome.Value!.Deliveries[0].DeliveredBiotaId, "A full-lot withdrawal of the only lot must deliver the original biota GUID.");
        Assert.AreEqual(25, redeemOutcome.Value!.Deliveries[0].Quantity);
        Assert.AreEqual(ownerId, redeemOutcome.Value!.FormerOwnerId);

        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, biotaId, recipientContainerId));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudStackLots.CountAsync(l => l.Id == lot.Id));
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.Id == depositOutcome.Value!.CustodyRecord.Id));

        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.Id == reserveOutcome.Value!.Id);
        Assert.AreEqual(CloudReservationStatus.Released, reservation.Status);
        Assert.AreEqual(CloudReservationReleaseReason.Fulfilled, reservation.ReleaseReason);

        Assert.IsNull(await boundary.TryGetActiveWithdrawalReservationAsync(tokenHash), "A fulfilled reservation is no longer active.");
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

        var first = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

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

        var outcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, Guid.NewGuid(), NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

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

        var first = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), idempotencyKey);
        var second = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.Id, second.Value!.Id);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudWithdrawalReservationTargets.CountAsync(t => t.StackLotId == lot.Id));
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

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, tokenHash, TimeSpan.FromTicks(1), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, NextId(), Guid.NewGuid());

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

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        var reservationId = reserveOutcome.Value!.Id;

        var cancelOutcome = await boundary.CancelWithdrawalReservationAsync(reservationId, expectedVersion: 1);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, cancelOutcome.Kind);
        Assert.AreEqual(CloudReservationStatus.Released, cancelOutcome.Value!.Status);

        var reopenOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
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
        await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());

        var first = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, idempotencyKey);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.Deliveries[0].DeliveredBiotaId, second.Value!.Deliveries[0].DeliveredBiotaId);

        var containerRowCount = await AceShardTestData.CountContainerRowsAsync(_fixture.AceShardConnectionString, biotaId);
        Assert.AreEqual(1, containerRowCount, "Replaying a committed redemption must not grant world possession a second time.");
    }

    [TestMethod]
    public async Task SplitLotAsync_OnALotWithAnActiveWithdrawalReservation_ReturnsConflict_AndLeavesTheLotUnchanged()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 100, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var lotAuthority = new CloudStackLotTransactionAuthority(context);
        var splitOutcome = await lotAuthority.SplitLotAsync(lot.Id, lot.Version, Guid.NewGuid(), quantityToSplit: 40);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, splitOutcome.Kind,
            "A lot backing an active Withdrawal Reservation must not be splittable out from under it.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(100, await verifyContext.CloudStackLots.Where(l => l.Id == lot.Id).Select(l => l.Quantity).SingleAsync());
        Assert.AreEqual(1, await verifyContext.CloudStackLots.CountAsync(l => l.CustodyRecordId == lot.CustodyRecordId), "No new lot must have been carved off.");
    }

    [TestMethod]
    public async Task TransferLotAsync_OnALotWithAnActiveWithdrawalReservation_ReturnsConflict_AndLeavesTheOwnerUnchanged()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 10, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var lotAuthority = new CloudStackLotTransactionAuthority(context);
        var transferOutcome = await lotAuthority.TransferLotAsync(lot.Id, lot.Version, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, transferOutcome.Kind,
            "A lot backing an active Withdrawal Reservation must not be reassignable out from under it.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(ownerId, await verifyContext.CloudStackLots.Where(l => l.Id == lot.Id).Select(l => l.OwnerId).SingleAsync());
    }

    [TestMethod]
    public async Task MergeLotsAsync_WhenTheMergeAwayLotHasAnActiveWithdrawalReservation_ReturnsConflict_AndLeavesBothLotsUnchanged()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 100, Guid.NewGuid());
        var keepLot = depositOutcome.Value!.Lot;

        var lotAuthority = new CloudStackLotTransactionAuthority(context);
        var splitOutcome = await lotAuthority.SplitLotAsync(keepLot.Id, keepLot.Version, ownerId, quantityToSplit: 40);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, splitOutcome.Kind, splitOutcome.Reason);
        var mergeLot = splitOutcome.Value!.NewLot;

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(mergeLot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var mergeOutcome = await lotAuthority.MergeLotsAsync(
            keepLot.Id, splitOutcome.Value!.RemainingLot.Version, mergeLot.Id, mergeLot.Version);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, mergeOutcome.Kind,
            "A lot backing an active Withdrawal Reservation must not be mergeable away from under it.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(2, await verifyContext.CloudStackLots.CountAsync(l => l.CustodyRecordId == keepLot.CustodyRecordId));
        Assert.AreEqual(40, await verifyContext.CloudStackLots.Where(l => l.Id == mergeLot.Id).Select(l => l.Quantity).SingleAsync());
    }

    [TestMethod]
    public async Task MergeLotsAsync_WhenTheKeepLotHasAnActiveWithdrawalReservation_ReturnsConflict_AndLeavesBothLotsUnchanged()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 100, Guid.NewGuid());
        var keepLot = depositOutcome.Value!.Lot;

        var lotAuthority = new CloudStackLotTransactionAuthority(context);
        var splitOutcome = await lotAuthority.SplitLotAsync(keepLot.Id, keepLot.Version, ownerId, quantityToSplit: 40);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, splitOutcome.Kind, splitOutcome.Reason);
        var mergeLot = splitOutcome.Value!.NewLot;
        var keepLotAfterSplit = splitOutcome.Value!.RemainingLot;

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(keepLot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var mergeOutcome = await lotAuthority.MergeLotsAsync(
            keepLot.Id, keepLotAfterSplit.Version, mergeLot.Id, mergeLot.Version);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, mergeOutcome.Kind,
            "A lot backing an active Withdrawal Reservation must not have another lot merged into it.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(2, await verifyContext.CloudStackLots.CountAsync(l => l.CustodyRecordId == keepLot.CustodyRecordId));
        Assert.AreEqual(60, await verifyContext.CloudStackLots.Where(l => l.Id == keepLot.Id).Select(l => l.Quantity).SingleAsync());
    }

    [TestMethod]
    public async Task Redeem_WhenTheReservedLotsQuantityDriftedSinceTheReservationWasOpened_ReturnsConflict_AndLeavesCustodyUnchanged()
    {
        var originalBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, originalBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var depositOutcome = await boundary.DepositStackAsync(originalBiotaId, ShardId, ownerId, 100, Guid.NewGuid());
        var keepLot = depositOutcome.Value!.Lot;

        var lotAuthority = new CloudStackLotTransactionAuthority(context);
        var splitOutcome = await lotAuthority.SplitLotAsync(keepLot.Id, keepLot.Version, ownerId, quantityToSplit: 40);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, splitOutcome.Kind, splitOutcome.Reason);
        var reservedLot = splitOutcome.Value!.NewLot;

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(reservedLot.Id)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        // Simulates the reserved lot's quantity drifting after the reservation captured it, by any
        // means other than CloudStackLotTransactionAuthority (which now refuses to touch a reserved
        // lot at all) -- e.g. an out-of-band integrity violation. Redemption must re-derive the
        // quantity to deliver from the lot it just locked, not trust the value captured at open time.
        await using (var driftContext = new CloudDbContext(options))
        {
            var trackedLot = await driftContext.CloudStackLots.SingleAsync(l => l.Id == reservedLot.Id);
            trackedLot.ReduceQuantity(30);
            await driftContext.SaveChangesAsync();
        }

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(
            tokenHash, NextId(), new Dictionary<Guid, uint> { [(await boundary.GetReservationTargetsAsync(reserveOutcome.Value!.Id)).Single().Id] = NextId() },
            Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, redeemOutcome.Kind,
            "A redemption must refuse to deliver a quantity the reserved lot no longer actually holds.");

        await using var verifyContext = new CloudDbContext(options);
        var remainingRecord = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.Id == depositOutcome.Value!.CustodyRecord.Id);
        Assert.AreEqual(100, remainingRecord.TotalQuantity, "A refused redemption must not reduce Cloud custody's total quantity.");
        Assert.AreEqual(10, await verifyContext.CloudStackLots.Where(l => l.Id == reservedLot.Id).Select(l => l.Quantity).SingleAsync());
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private static string NewTokenHash() => Convert.ToHexString(Guid.NewGuid().ToByteArray());
}
