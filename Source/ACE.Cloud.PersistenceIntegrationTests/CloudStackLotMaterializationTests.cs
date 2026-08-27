using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #5's materialization requirements (ARCH-010, INV-001, INV-003,
/// docs/adr/0002-defer-native-materialization-for-partial-stacks.md): full withdrawal delivers the
/// original native biota untouched, partial withdrawal materializes a new child biota under a
/// caller-supplied (ACE-allocated) GUID while the original GUID stays with the remainder, and every
/// materialization is logged as parent/child lineage. "Native stack limits" here means the native
/// PropertyInt.StackSize values recorded for the parent and child after a split always sum back to
/// the pre-split total -- materialization can neither create nor lose quantity.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStackLotMaterializationTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 900_000;

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
    public async Task FullWithdrawal_OfTheOnlyLot_DeliversTheOriginalBiota_WithoutMaterializingAChild()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var recipientContainerId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 25, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var withdrawOutcome = await boundary.WithdrawLotAsync(
            lot.Id, lot.Version, quantityToWithdraw: 25, recipientContainerId, materializedBiotaId: null, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, withdrawOutcome.Kind, withdrawOutcome.Reason);
        Assert.AreEqual(biotaId, withdrawOutcome.Value!.DeliveredBiotaId, "A full withdrawal of the last lot must deliver the original biota GUID, not a new one.");
        Assert.AreEqual(25, withdrawOutcome.Value!.Quantity);
        Assert.AreEqual(ownerId, withdrawOutcome.Value!.FormerOwnerId);

        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, biotaId, recipientContainerId));

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudStackLots.CountAsync(l => l.CustodyRecordId == depositOutcome.Value!.CustodyRecord.Id));
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.Id == depositOutcome.Value!.CustodyRecord.Id));
        Assert.AreEqual(0, await verifyContext.CloudStackLotLineageEvents.CountAsync(e => e.ParentBiotaId == biotaId), "No materialization occurred, so no lineage event should exist.");
    }

    [TestMethod]
    public async Task PartialWithdrawal_MaterializesAChild_AndKeepsTheOriginalGuidWithTheRemainder()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var recipientContainerId = NextId();
        var materializedBiotaId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 25, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var withdrawOutcome = await boundary.WithdrawLotAsync(
            lot.Id, lot.Version, quantityToWithdraw: 10, recipientContainerId, materializedBiotaId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, withdrawOutcome.Kind, withdrawOutcome.Reason);
        Assert.AreEqual(materializedBiotaId, withdrawOutcome.Value!.DeliveredBiotaId, "A partial withdrawal must deliver the new materialized child, never the original GUID.");
        Assert.AreEqual(10, withdrawOutcome.Value!.Quantity);

        // The original GUID remains in Cloud custody, off-world, holding the remainder (INV-003).
        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId), "The original biota must remain off-world (still Cloud-custodied).");
        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, materializedBiotaId, recipientContainerId), "The materialized child must be the one delivered to the recipient.");

        await using var verifyContext = new CloudDbContext(options);
        var remainingLot = await verifyContext.CloudStackLots.SingleAsync(l => l.CustodyRecordId == depositOutcome.Value!.CustodyRecord.Id);
        Assert.AreEqual(15, remainingLot.Quantity, "The remaining lot must keep exactly the un-withdrawn quantity, still owned by the same owner.");
        Assert.AreEqual(ownerId, remainingLot.OwnerId);

        var record = await verifyContext.CloudCustodyRecords.SingleAsync(r => r.Id == depositOutcome.Value!.CustodyRecord.Id);
        Assert.AreEqual(15, record.TotalQuantity);
    }

    [TestMethod]
    public async Task PartialWithdrawal_LogsParentChildLineage()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var materializedBiotaId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, ownerId, 25, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        await boundary.WithdrawLotAsync(lot.Id, lot.Version, quantityToWithdraw: 10, NextId(), materializedBiotaId, Guid.NewGuid());

        await using var verifyContext = new CloudDbContext(options);
        var lineageEvent = await verifyContext.CloudStackLotLineageEvents.SingleAsync(e => e.ChildBiotaId == materializedBiotaId);
        Assert.AreEqual(biotaId, lineageEvent.ParentBiotaId);
        Assert.AreEqual(10, lineageEvent.Quantity);
        Assert.AreEqual(ownerId, lineageEvent.OwnerId);
        Assert.AreEqual(ShardId, lineageEvent.ShardId);
    }

    [TestMethod]
    public async Task PartialWithdrawal_ParentAndChildStackSizes_SumToThePreSplitTotal_NativeStackLimits()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var materializedBiotaId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 40, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        await boundary.WithdrawLotAsync(lot.Id, lot.Version, quantityToWithdraw: 17, NextId(), materializedBiotaId, Guid.NewGuid());

        var parentStackSize = await AceShardTestData.GetStackSizeAsync(_fixture.AceShardConnectionString, biotaId);
        var childStackSize = await AceShardTestData.GetStackSizeAsync(_fixture.AceShardConnectionString, materializedBiotaId);

        Assert.AreEqual(23, parentStackSize, "The remaining parent stack size must reflect exactly what was not withdrawn.");
        Assert.AreEqual(17, childStackSize, "The materialized child's stack size must equal exactly the withdrawn quantity.");
        Assert.AreEqual(40, parentStackSize + childStackSize, "Materialization must neither create nor lose native stack quantity.");
    }

    [TestMethod]
    public async Task WithdrawingALotEntirely_WhileSiblingLotsRemain_StillMaterializesAChild()
    {
        // The withdrawn lot is fully consumed (quantityToWithdraw == lot.Quantity), but because
        // sibling lots still hold the rest of the stack in Cloud custody, this is not a "full
        // stack" withdrawal: the original GUID must stay with the siblings' remainder, so the
        // withdrawn lot still needs its own materialized child (INV-003's remainder preference is
        // about the whole stack, not about whichever lot happens to be withdrawn first).
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var materializedBiotaId = NextId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var authority = new CloudStackLotTransactionAuthority(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 30, Guid.NewGuid());
        var splitOutcome = await authority.SplitLotAsync(depositOutcome.Value!.Lot.Id, depositOutcome.Value!.Lot.Version, Guid.NewGuid(), 12);
        var lotToWithdraw = splitOutcome.Value!.NewLot;

        var withdrawOutcome = await boundary.WithdrawLotAsync(
            lotToWithdraw.Id, lotToWithdraw.Version, quantityToWithdraw: 12, NextId(), materializedBiotaId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, withdrawOutcome.Kind, withdrawOutcome.Reason);
        Assert.AreEqual(materializedBiotaId, withdrawOutcome.Value!.DeliveredBiotaId);
        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId), "The original biota must remain custodied for the surviving sibling lot.");

        await using var verifyContext = new CloudDbContext(options);
        var remainingLots = await verifyContext.CloudStackLots.Where(l => l.CustodyRecordId == depositOutcome.Value!.CustodyRecord.Id).ToListAsync();
        Assert.HasCount(1, remainingLots);
        Assert.AreEqual(18, remainingLots[0].Quantity);
    }

    [TestMethod]
    public async Task WithdrawLot_RequiresAMaterializedBiotaId_WhenTheStackWouldSurviveTheWithdrawal()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 25, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var outcome = await boundary.WithdrawLotAsync(lot.Id, lot.Version, quantityToWithdraw: 5, NextId(), materializedBiotaId: null, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "materialized");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(25, (await verifyContext.CloudStackLots.SingleAsync(l => l.Id == lot.Id)).Quantity, "A rejected withdrawal must not mutate the lot.");
    }

    [TestMethod]
    public async Task RepeatedIdempotencyKey_ForLotWithdrawal_ReplaysCommittedResult_WithoutMaterializingTwice()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var materializedBiotaId = NextId();
        var recipientContainerId = NextId();
        var idempotencyKey = Guid.NewGuid();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), 25, Guid.NewGuid());
        var lot = depositOutcome.Value!.Lot;

        var first = await boundary.WithdrawLotAsync(lot.Id, lot.Version, 10, recipientContainerId, materializedBiotaId, idempotencyKey);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);

        var second = await boundary.WithdrawLotAsync(lot.Id, lot.Version, 10, recipientContainerId, materializedBiotaId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.DeliveredBiotaId, second.Value!.DeliveredBiotaId);
        Assert.AreEqual(first.Value!.Quantity, second.Value!.Quantity);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudStackLotLineageEvents.CountAsync(e => e.ChildBiotaId == materializedBiotaId), "Replaying a committed withdrawal must not log a second lineage event.");

        var childStackSize = await AceShardTestData.GetStackSizeAsync(_fixture.AceShardConnectionString, materializedBiotaId);
        Assert.AreEqual(10, childStackSize, "Replay must not re-apply the materialization a second time.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
