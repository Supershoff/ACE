using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #122's Red section: "Test mixed selections for... a crash at every commit boundary; every
/// failure must deliver nothing and retain the complete retryable reservation where required."
/// Proves that a simulated process crash at every named <see cref="CloudBoundaryFaultPoint"/> during
/// a <em>multi-target</em> reservation open or redemption rolls back every target's mutation, not
/// only the one being processed at the moment of the crash, and that retrying with the same
/// idempotency key afterward recovers cleanly to the same committed result Deposit/Withdrawal
/// crash-safety tests already established for single-target operations
/// (<see cref="CloudPhaseGateAcceptanceTests"/>).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudWithdrawalReservationFaultInjectionTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 1_200_000;

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
    public async Task CrashDuringMixedReserve_AfterCustodyChange_RollsBackBothTargets_ThenRetryWithSameKeyCommitsBoth()
    {
        var itemBiotaId = NextId();
        var stackBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, itemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var reserveIdempotencyKey = Guid.NewGuid();

        await using var setupContext = new CloudDbContext(options);
        var setupBoundary = new CloudCustodyBoundary(setupContext);
        await setupBoundary.DepositAsync(itemBiotaId, ShardId, ownerId, Guid.NewGuid());
        var stackDeposit = await setupBoundary.DepositStackAsync(stackBiotaId, ShardId, ownerId, 10, Guid.NewGuid());
        var lotId = stackDeposit.Value!.Lot.Id;

        var targets = new[]
        {
            CloudWithdrawalReservationRequestTarget.ForItem(itemBiotaId),
            CloudWithdrawalReservationRequestTarget.ForStackLot(lotId),
        };

        await using var crashingContext = new CloudDbContext(options);
        var crashingBoundary = new CloudCustodyBoundary(crashingContext);

        Func<CloudBoundaryFaultPoint, Task> crashAfterCustodyChange = point =>
            point == CloudBoundaryFaultPoint.AfterCustodyChange
                ? throw new CloudBoundarySimulatedCrashException(point)
                : Task.CompletedTask;

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(
            () => crashingBoundary.ReserveForWithdrawalAsync(
                targets, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), reserveIdempotencyKey, crashAfterCustodyChange, CancellationToken.None));

        await using (var verifyContext = new CloudDbContext(options))
        {
            Assert.AreEqual(0, await verifyContext.CloudWithdrawalReservations.CountAsync(r => r.TokenHash == tokenHash), "The crashed reserve attempt must have rolled back completely.");
            Assert.AreEqual(0, await verifyContext.CloudWithdrawalReservationTargets.CountAsync(t => t.ItemBiotaId == itemBiotaId), "Neither target may end up reserved.");
            Assert.AreEqual(0, await verifyContext.CloudWithdrawalReservationTargets.CountAsync(t => t.StackLotId == lotId));
        }

        await using var retryContext = new CloudDbContext(options);
        var retryBoundary = new CloudCustodyBoundary(retryContext);
        var retryOutcome = await retryBoundary.ReserveForWithdrawalAsync(
            targets, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), reserveIdempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, retryOutcome.Kind, retryOutcome.Reason);
        Assert.HasCount(2, await retryBoundary.GetReservationTargetsAsync(retryOutcome.Value!.Id));
    }

    [TestMethod]
    public async Task CrashDuringMixedRedeem_WhileProcessingTheSecondTarget_RollsBackTheFirstTargetsAlreadyStagedRelease_ThenRetryWithSameKeyDeliversBoth()
    {
        var firstBiotaId = NextId();
        var secondBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();
        var redeemIdempotencyKey = Guid.NewGuid();

        await using (var setupContext = new CloudDbContext(options))
        {
            var setupBoundary = new CloudCustodyBoundary(setupContext);
            await setupBoundary.DepositAsync(firstBiotaId, ShardId, ownerId, Guid.NewGuid());
            await setupBoundary.DepositAsync(secondBiotaId, ShardId, ownerId, Guid.NewGuid());

            var reserveOutcome = await setupBoundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(firstBiotaId), CloudWithdrawalReservationRequestTarget.ForItem(secondBiotaId)],
                ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
        }

        // Crashes on the SECOND occurrence of AfterCustodyChange: the first target's custody release
        // has already been staged (SaveChanges'd within the still-open transaction) when this fires.
        var custodyChangeCount = 0;
        Func<CloudBoundaryFaultPoint, Task> crashOnSecondCustodyChange = point =>
        {
            if (point != CloudBoundaryFaultPoint.AfterCustodyChange)
            {
                return Task.CompletedTask;
            }

            custodyChangeCount++;
            return custodyChangeCount == 2 ? throw new CloudBoundarySimulatedCrashException(point) : Task.CompletedTask;
        };

        await using var crashingContext = new CloudDbContext(options);
        var crashingBoundary = new CloudCustodyBoundary(crashingContext);

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(
            () => crashingBoundary.RedeemWithdrawalReservationAsync(
                tokenHash, recipientContainerId, EmptyMaterializedBiotaIds,
                redeemIdempotencyKey, crashOnSecondCustodyChange, CancellationToken.None));

        // Neither target may have been delivered, and neither Cloud Custody Record may have been
        // released, even though the first target's release had already been staged in memory before
        // the crash: the whole multi-target redemption shares one uncommitted transaction.
        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, firstBiotaId));
        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, secondBiotaId));

        await using (var verifyContext = new CloudDbContext(options))
        {
            Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == firstBiotaId), "The first target's Cloud Custody Record must survive the crashed redemption.");
            Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == secondBiotaId));

            var reservation = await verifyContext.CloudWithdrawalReservations.AsNoTracking().SingleAsync(r => r.TokenHash == tokenHash);
            Assert.AreEqual(CloudReservationStatus.Active, reservation.Status, "The reservation must remain active and retryable after a crashed redemption.");
        }

        // A "restarted caller" retries with the same idempotency key and actually commits this time.
        await using var retryContext = new CloudDbContext(options);
        var retryBoundary = new CloudCustodyBoundary(retryContext);
        var retryOutcome = await retryBoundary.RedeemWithdrawalReservationAsync(
            tokenHash, recipientContainerId, EmptyMaterializedBiotaIds, redeemIdempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, retryOutcome.Kind, retryOutcome.Reason);
        Assert.HasCount(2, retryOutcome.Value!.Deliveries);
        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, firstBiotaId, recipientContainerId));
        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, secondBiotaId, recipientContainerId));
    }

    [TestMethod]
    public async Task CrashDuringRedeem_BeforeCommit_ThenRetryWithSameKey_RecoversAndDeliversTheItem()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = NewTokenHash();
        var recipientContainerId = NextId();
        var redeemIdempotencyKey = Guid.NewGuid();

        await using (var setupContext = new CloudDbContext(options))
        {
            var setupBoundary = new CloudCustodyBoundary(setupContext);
            await setupBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid());
            var reserveOutcome = await setupBoundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
        }

        Func<CloudBoundaryFaultPoint, Task> crashBeforeCommit = point =>
            point == CloudBoundaryFaultPoint.BeforeCommit
                ? throw new CloudBoundarySimulatedCrashException(point)
                : Task.CompletedTask;

        await using var crashingContext = new CloudDbContext(options);
        var crashingBoundary = new CloudCustodyBoundary(crashingContext);

        await Assert.ThrowsExactlyAsync<CloudBoundarySimulatedCrashException>(
            () => crashingBoundary.RedeemWithdrawalReservationAsync(
                tokenHash, recipientContainerId, EmptyMaterializedBiotaIds,
                redeemIdempotencyKey, crashBeforeCommit, CancellationToken.None));

        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId), "A crash strictly before commit must never have applied the grant.");

        await using var retryContext = new CloudDbContext(options);
        var retryBoundary = new CloudCustodyBoundary(retryContext);
        var retryOutcome = await retryBoundary.RedeemWithdrawalReservationAsync(
            tokenHash, recipientContainerId, EmptyMaterializedBiotaIds, redeemIdempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, retryOutcome.Kind, retryOutcome.Reason);
        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, biotaId, recipientContainerId));
    }

    private static readonly IReadOnlyDictionary<Guid, uint> EmptyMaterializedBiotaIds = new Dictionary<Guid, uint>();

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private static string NewTokenHash() => Convert.ToHexString(Guid.NewGuid().ToByteArray());
}
