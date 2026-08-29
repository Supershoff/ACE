using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #122's unified, atomic multi-item Withdrawal Token
/// (WDR-001, WDR-003, WDR-004, WDR-005, WDR-008, INV-002, INV-003, ARCH-002, ARCH-006): one parent
/// <see cref="CloudWithdrawalReservation"/> aggregate reserves and later redeems an arbitrary mixed
/// selection of whole Cloud Items and Cloud Stack Lot quantities atomically. Before this issue, a
/// whole-item reservation and a Cloud Stack Lot reservation were two independent tables, each with
/// its own <c>TokenHash</c> uniqueness constraint -- so the same token secret could address two
/// different, independently consumable reservations at once, and no single token could name more
/// than one whole item, one item plus one partial lot, or several partial lots. Every test below
/// exercises the merged aggregate directly; <see cref="CloudWithdrawalReservationFaultInjectionTests"/>
/// covers crash-at-every-commit-boundary evidence for multi-target reserve/redeem, and
/// <see cref="CloudStackLotWithdrawalReservationTests"/> covers the Cloud Stack Lot Transaction
/// Authority interlock and quantity-drift guard.
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
    public async Task ReserveForWithdrawal_OneWholeItem_ThenRedeem_DeliversTheItem_ReleasesTheReservationAsFulfilled_AndAppendsLedgerAndOutbox()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await boundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
        Assert.AreEqual(CloudReservationStatus.Active, reserveOutcome.Value!.Status);

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
        Assert.HasCount(1, redeemOutcome.Value!.Deliveries);
        Assert.AreEqual(biotaId, redeemOutcome.Value!.Deliveries[0].DeliveredBiotaId);
        Assert.IsNull(redeemOutcome.Value!.Deliveries[0].Quantity);
        Assert.AreEqual(recipientContainerId, redeemOutcome.Value!.RecipientContainerId);
        Assert.AreEqual(ownerId, redeemOutcome.Value!.FormerOwnerId);

        Assert.IsTrue(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));

        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.Id == reserveOutcome.Value!.Id);
        Assert.AreEqual(CloudReservationStatus.Released, reservation.Status);
        Assert.AreEqual(CloudReservationReleaseReason.Fulfilled, reservation.ReleaseReason);

        var ledgerEvents = await verifyContext.CloudActivityLedgerEvents.Where(e => e.BiotaId == biotaId).ToListAsync();
        Assert.HasCount(3, ledgerEvents, "Deposit, ReservationOpened, and Withdrawal (redemption) must each append one ledger event.");

        var outboxEvents = await verifyContext.CloudCustodyOutboxEvents.Where(e => e.BiotaId == biotaId).ToListAsync();
        Assert.HasCount(3, outboxEvents);

        Assert.IsNull(await boundary.TryGetActiveWithdrawalReservationAsync(tokenHash), "A fulfilled reservation is no longer active.");
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_TwoMixedWholeItems_ThenRedeem_DeliversBothAtomically()
    {
        var firstBiotaId = NextId();
        var secondBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(firstBiotaId, ShardId, ownerId, Guid.NewGuid());
        await boundary.DepositAsync(secondBiotaId, ShardId, ownerId, Guid.NewGuid());

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(firstBiotaId), CloudWithdrawalReservationRequestTarget.ForItem(secondBiotaId)],
            ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var targets = await boundary.GetReservationTargetsAsync(reserveOutcome.Value!.Id);
        Assert.HasCount(2, targets, "One token now represents a mixed multi-item selection as a single reservation aggregate.");

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
        var deliveredBiotaIds = redeemOutcome.Value!.Deliveries.Select(d => d.DeliveredBiotaId).ToHashSet();
        Assert.HasCount(2, deliveredBiotaIds);
        Assert.IsTrue(deliveredBiotaIds.Contains(firstBiotaId));
        Assert.IsTrue(deliveredBiotaIds.Contains(secondBiotaId));

        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, firstBiotaId, recipientContainerId));
        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, secondBiotaId, recipientContainerId));
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_OneItemPlusOnePartialStackLot_ThenRedeem_DeliversBothAndMaterializesTheLotsChild()
    {
        var itemBiotaId = NextId();
        var originalStackBiotaId = NextId();
        var materializedBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, itemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, originalStackBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(itemBiotaId, ShardId, ownerId, Guid.NewGuid());
        var stackDeposit = await boundary.DepositStackAsync(originalStackBiotaId, ShardId, ownerId, 100, Guid.NewGuid());
        var lot = stackDeposit.Value!.Lot;

        var lotAuthority = new CloudStackLotTransactionAuthority(context);
        var splitOutcome = await lotAuthority.SplitLotAsync(lot.Id, lot.Version, ownerId, quantityToSplit: 40);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, splitOutcome.Kind, splitOutcome.Reason);
        var splitLotId = splitOutcome.Value!.NewLot.Id;

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(itemBiotaId), CloudWithdrawalReservationRequestTarget.ForStackLot(splitLotId)],
            ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var previews = await boundary.PreviewWithdrawalReservationAsync(tokenHash);
        Assert.HasCount(2, previews!);
        var lotPreview = previews!.Single(p => p.Kind == CloudWithdrawalReservationTargetKind.StackLot);
        Assert.IsTrue(lotPreview.RequiresMaterialization, "The split-off lot is not the sole lot on its stack, so it must require materialization.");
        Assert.AreEqual(40, lotPreview.Quantity);

        var materializedBiotaIdsByTargetId = new Dictionary<Guid, uint> { [lotPreview.TargetId] = materializedBiotaId };
        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(
            tokenHash, recipientContainerId, materializedBiotaIdsByTargetId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
        Assert.HasCount(2, redeemOutcome.Value!.Deliveries);

        var itemDelivery = redeemOutcome.Value!.Deliveries.Single(d => d.DeliveredBiotaId == itemBiotaId);
        Assert.IsNull(itemDelivery.Quantity);

        var lotDelivery = redeemOutcome.Value!.Deliveries.Single(d => d.DeliveredBiotaId == materializedBiotaId);
        Assert.AreEqual(40, lotDelivery.Quantity);

        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, itemBiotaId, recipientContainerId));
        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, materializedBiotaId, recipientContainerId));
        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, originalStackBiotaId), "The remaining 60 stays in Cloud custody, not world possession.");

        await using var verifyContext = new CloudDbContext(options);
        var remainingRecord = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.Id == stackDeposit.Value!.CustodyRecord.Id);
        Assert.AreEqual(60, remainingRecord.TotalQuantity);
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_SeveralPartialStackLots_ThenRedeem_DeliversEveryMaterializedChild()
    {
        var originalBiotaId = NextId();
        var firstMaterializedBiotaId = NextId();
        var secondMaterializedBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, originalBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var stackDeposit = await boundary.DepositStackAsync(originalBiotaId, ShardId, ownerId, 100, Guid.NewGuid());
        var keepLot = stackDeposit.Value!.Lot;

        var lotAuthority = new CloudStackLotTransactionAuthority(context);
        var firstSplit = await lotAuthority.SplitLotAsync(keepLot.Id, keepLot.Version, ownerId, quantityToSplit: 20);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, firstSplit.Kind, firstSplit.Reason);
        var secondSplit = await lotAuthority.SplitLotAsync(
            firstSplit.Value!.RemainingLot.Id, firstSplit.Value!.RemainingLot.Version, ownerId, quantityToSplit: 30);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, secondSplit.Kind, secondSplit.Reason);

        var firstLotId = firstSplit.Value!.NewLot.Id;
        var secondLotId = secondSplit.Value!.NewLot.Id;

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(firstLotId), CloudWithdrawalReservationRequestTarget.ForStackLot(secondLotId)],
            ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var previews = await boundary.PreviewWithdrawalReservationAsync(tokenHash);
        Assert.HasCount(2, previews!);
        Assert.IsTrue(previews!.All(p => p.RequiresMaterialization), "Every reserved lot still has siblings left on the stack.");

        var materializedBiotaIdsByTargetId = new Dictionary<Guid, uint>
        {
            [previews!.Single(p => p.Quantity == 20).TargetId] = firstMaterializedBiotaId,
            [previews!.Single(p => p.Quantity == 30).TargetId] = secondMaterializedBiotaId,
        };

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(
            tokenHash, recipientContainerId, materializedBiotaIdsByTargetId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);
        Assert.HasCount(2, redeemOutcome.Value!.Deliveries);

        var deliveredQuantitiesByBiota = redeemOutcome.Value!.Deliveries.ToDictionary(d => d.DeliveredBiotaId, d => d.Quantity);
        Assert.AreEqual(20, deliveredQuantitiesByBiota[firstMaterializedBiotaId]);
        Assert.AreEqual(30, deliveredQuantitiesByBiota[secondMaterializedBiotaId]);

        await using var verifyContext = new CloudDbContext(options);
        var remainingRecord = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.Id == stackDeposit.Value!.CustodyRecord.Id);
        Assert.AreEqual(50, remainingRecord.TotalQuantity, "100 deposited minus the 20 and 30 redeemed must leave exactly 50 in Cloud custody.");
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_MixedSelectionWhereOneLotTargetIsAlreadyReserved_RefusesTheWholeRequest_AndLeavesTheItemTargetFreelyReservable()
    {
        var itemBiotaId = NextId();
        var stackBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, itemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(itemBiotaId, ShardId, ownerId, Guid.NewGuid());
        var stackDeposit = await boundary.DepositStackAsync(stackBiotaId, ShardId, ownerId, 10, Guid.NewGuid());
        var lot = stackDeposit.Value!.Lot;

        var priorReservation = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, priorReservation.Kind, priorReservation.Reason);

        var mixedOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(itemBiotaId), CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)],
            ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, mixedOutcome.Kind, "One already-reserved target must refuse the entire mixed request (all-or-none).");

        var itemOnlyOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(itemBiotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed, itemOnlyOutcome.Kind,
            "The item target must remain freely reservable: the refused mixed request must not have partially reserved it.");
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_TheSameTokenHashCanNoLongerAddressTwoIndependentReservations()
    {
        // Regression coverage for the exact defect this issue corrects: previously, a whole-item
        // reservation and a Cloud Stack Lot reservation were two independent tables, each with its
        // own TokenHash unique constraint, so the same secret could open one row in each at once.
        var itemBiotaId = NextId();
        var stackBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, itemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var sharedTokenHash = NewTokenHash();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(itemBiotaId, ShardId, ownerId, Guid.NewGuid());
        var stackDeposit = await boundary.DepositStackAsync(stackBiotaId, ShardId, ownerId, 10, Guid.NewGuid());
        var lot = stackDeposit.Value!.Lot;

        var itemReservation = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(itemBiotaId)], ShardId, ownerId, sharedTokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, itemReservation.Kind, itemReservation.Reason);

        var lotReservationReusingSameToken = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(lot.Id)], ShardId, ownerId, sharedTokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Conflict, lotReservationReusingSameToken.Kind,
            "One shared TokenHash uniqueness constraint must reject a second reservation for the same secret, regardless of target kind.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudWithdrawalReservations.CountAsync(r => r.TokenHash == sharedTokenHash));
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
        var first = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

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

        var first = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), idempotencyKey);
        var second = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.Id, second.Value!.Id);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudWithdrawalReservations.CountAsync(r => r.TokenHash == tokenHash));
        Assert.AreEqual(1, await verifyContext.CloudWithdrawalReservationTargets.CountAsync(t => t.ItemBiotaId == biotaId));
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
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, Guid.NewGuid(), NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task ReserveForWithdrawal_ForAStackCustodyRecordTargetedAsAWholeItem_ReturnsConflict()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        await boundary.DepositStackAsync(biotaId, ShardId, ownerId, quantity: 10, Guid.NewGuid());

        var outcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

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
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, tokenHash, TimeSpan.FromTicks(1), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind);
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, redeemOutcome.Kind);
        StringAssert.Contains(redeemOutcome.Reason, "expired");

        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));

        await using var verifyContext = new CloudDbContext(options);
        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.Id == reserveOutcome.Value!.Id);
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
        await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());

        var first = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, idempotencyKey);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.Deliveries[0].DeliveredBiotaId, second.Value!.Deliveries[0].DeliveredBiotaId);

        var containerRowCount = await AceShardTestData.CountContainerRowsAsync(_fixture.AceShardConnectionString, biotaId);
        Assert.AreEqual(1, containerRowCount, "Replaying a committed redemption must not grant world possession a second time.");
    }

    [TestMethod]
    public async Task Redeem_AMixedSelectionMissingARequiredMaterializedGuid_RefusesTheWholeRedemption_AndLeavesEveryTargetsCustodyUnchanged()
    {
        var itemBiotaId = NextId();
        var stackBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, itemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(itemBiotaId, ShardId, ownerId, Guid.NewGuid());
        var stackDeposit = await boundary.DepositStackAsync(stackBiotaId, ShardId, ownerId, 100, Guid.NewGuid());
        var lot = stackDeposit.Value!.Lot;

        var lotAuthority = new CloudStackLotTransactionAuthority(context);
        var splitOutcome = await lotAuthority.SplitLotAsync(lot.Id, lot.Version, ownerId, quantityToSplit: 40);
        var splitLotId = splitOutcome.Value!.NewLot.Id;

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(itemBiotaId), CloudWithdrawalReservationRequestTarget.ForStackLot(splitLotId)],
            ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        // No materialized GUID supplied at all for the not-sole lot target: the whole redemption
        // must refuse, including the item target that was otherwise perfectly redeemable.
        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, redeemOutcome.Kind);
        StringAssert.Contains(redeemOutcome.Reason, "materialized");

        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, itemBiotaId), "All-or-none: the item target must not have been delivered either.");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == itemBiotaId), "The item's Cloud Custody Record must survive a refused mixed redemption.");
        Assert.AreEqual(100, (await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.Id == stackDeposit.Value!.CustodyRecord.Id)).TotalQuantity);

        var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.Id == reserveOutcome.Value!.Id);
        Assert.AreEqual(CloudReservationStatus.Active, reservation.Status, "A refused redemption must leave the reservation retryable.");
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
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        var reservationId = reserveOutcome.Value!.Id;

        var cancelOutcome = await boundary.CancelWithdrawalReservationAsync(reservationId, expectedVersion: 1);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, cancelOutcome.Kind);
        Assert.AreEqual(CloudReservationStatus.Released, cancelOutcome.Value!.Status);
        Assert.AreEqual(CloudReservationReleaseReason.Cancelled, cancelOutcome.Value!.ReleaseReason);

        var reopenOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reopenOutcome.Kind, "Cancelling must free the biota for a fresh reservation.");
    }

    [TestMethod]
    public async Task Cancel_AMixedReservation_ReleasesEveryTarget()
    {
        var itemBiotaId = NextId();
        var stackBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, itemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(itemBiotaId, ShardId, ownerId, Guid.NewGuid());
        var stackDeposit = await boundary.DepositStackAsync(stackBiotaId, ShardId, ownerId, 10, Guid.NewGuid());

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(itemBiotaId), CloudWithdrawalReservationRequestTarget.ForStackLot(stackDeposit.Value!.Lot.Id)],
            ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var cancelOutcome = await boundary.CancelWithdrawalReservationAsync(reserveOutcome.Value!.Id, expectedVersion: 1);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, cancelOutcome.Kind);

        var reopenItem = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForItem(itemBiotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
        var reopenLot = await boundary.ReserveForWithdrawalAsync(
            [CloudWithdrawalReservationRequestTarget.ForStackLot(stackDeposit.Value!.Lot.Id)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reopenItem.Kind, "Cancelling a mixed reservation must free every target it held, including the item.");
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reopenLot.Kind, "Cancelling a mixed reservation must free every target it held, including the lot.");
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
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, NewTokenHash(), TimeSpan.FromMinutes(15), Guid.NewGuid());
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
            [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        var reservationId = reserveOutcome.Value!.Id;

        await boundary.RedeemWithdrawalReservationAsync(tokenHash, NextId(), Guid.NewGuid());

        var cancelOutcome = await boundary.CancelWithdrawalReservationAsync(reservationId, expectedVersion: 1);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, cancelOutcome.Kind);
        StringAssert.Contains(cancelOutcome.Reason, "Fulfilled");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private static string NewTokenHash() => Convert.ToHexString(Guid.NewGuid().ToByteArray());
}
