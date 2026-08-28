using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #14's Raw Pyreal Deposit conversion (DEP-006): boundary/property
/// conservation, idempotency under retry, MMD custody creation, remainder persistence, Storage Quota
/// exclusion (INV-004), and concurrent-conflict revalidation.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudPyrealConversionBoundaryTests
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

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private async Task<uint> InsertRawPyrealBiotaAsync(long value)
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, biotaId, value);
        return biotaId;
    }

    [TestMethod]
    public async Task ConvertPyrealDeposit_BelowThreshold_CreatesNoMmdsAndPersistsTheExactRemainder()
    {
        var rawBiotaId = await InsertRawPyrealBiotaAsync(100_000);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var ownerId = Guid.NewGuid();
        var outcome = await boundary.ConvertPyrealDepositAsync(rawBiotaId, ShardId, ownerId, 100_000, mmdBiotaIds: [], Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.IsEmpty(outcome.Value!.MmdCustodyRecords);
        Assert.AreEqual(100_000, outcome.Value.NewRemainder);

        await using var verifyContext = new CloudDbContext(options);
        var remainder = await verifyContext.CloudPyrealRemainders.AsNoTracking()
            .SingleAsync(r => r.OwnerId == ownerId && r.ShardId == ShardId);
        Assert.AreEqual(100_000, remainder.RemainderAmount);

        // INV-004: a Pyreal Remainder never counts toward Storage Quota.
        Assert.AreEqual(0, await CloudStackQuotaProjection.CountProjectedItemsAsync(verifyContext, ShardId, ownerId));

        // The consumed raw biota is gone; it was never held under a Cloud Custody Record.
        Assert.IsFalse(await AceShardTestData.BiotaExistsAsync(_fixture.AceShardConnectionString, rawBiotaId));
    }

    [TestMethod]
    public async Task ConvertPyrealDeposit_ExactlyOneThreshold_CreatesExactlyOneMmdAndNoRemainder()
    {
        var rawBiotaId = await InsertRawPyrealBiotaAsync(PyrealConversionPolicy.PyrealsPerMmd);
        var mmdBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, mmdBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var ownerId = Guid.NewGuid();
        var outcome = await boundary.ConvertPyrealDepositAsync(
            rawBiotaId, ShardId, ownerId, PyrealConversionPolicy.PyrealsPerMmd, [mmdBiotaId], Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.HasCount(1, outcome.Value!.MmdCustodyRecords);
        Assert.AreEqual(mmdBiotaId, outcome.Value.MmdCustodyRecords[0].BiotaId);
        Assert.AreEqual(ownerId, outcome.Value.MmdCustodyRecords[0].OwnerId);
        Assert.AreEqual(0, outcome.Value.NewRemainder);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == mmdBiotaId));

        // The MMD itself is an ordinary Cloud Item and does count toward quota, unlike the remainder.
        Assert.AreEqual(1, await CloudStackQuotaProjection.CountProjectedItemsAsync(verifyContext, ShardId, ownerId));

        // Each MMD also gets an ordinary Deposit-typed ledger/outbox event.
        Assert.AreEqual(1, await verifyContext.CloudActivityLedgerEvents.CountAsync(
            e => e.BiotaId == mmdBiotaId && e.EventType == CloudBoundaryOperationType.Deposit));
        Assert.AreEqual(1, await verifyContext.CloudCustodyOutboxEvents.CountAsync(
            e => e.BiotaId == mmdBiotaId && e.EventType == CloudBoundaryOperationType.Deposit));

        // The conversion itself is also recorded against the consumed raw biota.
        Assert.AreEqual(1, await verifyContext.CloudActivityLedgerEvents.CountAsync(
            e => e.BiotaId == rawBiotaId && e.EventType == CloudBoundaryOperationType.PyrealConversion));
    }

    [TestMethod]
    public async Task ConvertPyrealDeposit_CombinesWithAnExistingRemainderBeforeConverting()
    {
        var ownerId = Guid.NewGuid();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        var firstRawBiotaId = await InsertRawPyrealBiotaAsync(200_000);
        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var first = await boundary.ConvertPyrealDepositAsync(firstRawBiotaId, ShardId, ownerId, 200_000, mmdBiotaIds: [], Guid.NewGuid());
            Assert.AreEqual(200_000, first.Value!.NewRemainder);
        }

        // A second deposit of 100,000 combines with the 200,000 remainder: 300,000 total => 1 MMD
        // (287,500) + 12,500 remainder.
        var secondRawBiotaId = await InsertRawPyrealBiotaAsync(100_000);
        var mmdBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, mmdBiotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var second = await boundary.ConvertPyrealDepositAsync(secondRawBiotaId, ShardId, ownerId, 100_000, [mmdBiotaId], Guid.NewGuid());

            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
            Assert.HasCount(1, second.Value!.MmdCustodyRecords);
            Assert.AreEqual(12_500, second.Value.NewRemainder);
        }
    }

    [TestMethod]
    public async Task ConvertPyrealDeposit_RepeatedIdempotencyKey_ReplaysTheCommittedResult()
    {
        var rawBiotaId = await InsertRawPyrealBiotaAsync(PyrealConversionPolicy.PyrealsPerMmd);
        var mmdBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, mmdBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var ownerId = Guid.NewGuid();
        var idempotencyKey = Guid.NewGuid();

        var first = await boundary.ConvertPyrealDepositAsync(
            rawBiotaId, ShardId, ownerId, PyrealConversionPolicy.PyrealsPerMmd, [mmdBiotaId], idempotencyKey);
        var second = await boundary.ConvertPyrealDepositAsync(
            rawBiotaId, ShardId, ownerId, PyrealConversionPolicy.PyrealsPerMmd, [mmdBiotaId], idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(first.Value!.MmdCustodyRecords[0].Id, second.Value!.MmdCustodyRecords[0].Id);
        Assert.AreEqual(first.Value.NewRemainder, second.Value.NewRemainder);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == mmdBiotaId));
        Assert.AreEqual(1, await verifyContext.CloudPyrealConversionRecords.CountAsync(r => r.IdempotencyKey == idempotencyKey));
    }

    [TestMethod]
    public async Task ConvertPyrealDeposit_AMismatchedMmdCount_RefusesAndChangesNothing()
    {
        var rawBiotaId = await InsertRawPyrealBiotaAsync(PyrealConversionPolicy.PyrealsPerMmd);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var ownerId = Guid.NewGuid();

        // 287,500 raw Pyreals requires exactly one MMD; supplying zero must be refused rather than
        // silently under-converting or losing the value.
        var outcome = await boundary.ConvertPyrealDepositAsync(
            rawBiotaId, ShardId, ownerId, PyrealConversionPolicy.PyrealsPerMmd, mmdBiotaIds: [], Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);

        await using var verifyContext = new CloudDbContext(options);
        Assert.IsTrue(await AceShardTestData.BiotaExistsAsync(_fixture.AceShardConnectionString, rawBiotaId), "A refused conversion must not consume the raw Pyreal biota.");
        Assert.IsFalse(await verifyContext.CloudPyrealRemainders.AnyAsync(r => r.OwnerId == ownerId));
    }

    [TestMethod]
    public async Task ConvertPyrealDeposit_ConcurrentConversionsForTheSameOwner_OnlyOneUsesTheOriginalRemainder()
    {
        var ownerId = Guid.NewGuid();

        var rawBiotaIdA = await InsertRawPyrealBiotaAsync(200_000);
        var rawBiotaIdB = await InsertRawPyrealBiotaAsync(200_000);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var contextA = new CloudDbContext(options);
        await using var contextB = new CloudDbContext(options);
        var boundaryA = new CloudCustodyBoundary(contextA);
        var boundaryB = new CloudCustodyBoundary(contextB);

        // Both attempts assume a starting remainder of 0 and therefore neither MMD is needed for
        // either individual 200,000 deposit; this proves the remainder row lock serializes them
        // (one sees the other's committed remainder) rather than losing one deposit's value.
        var taskA = boundaryA.ConvertPyrealDepositAsync(rawBiotaIdA, ShardId, ownerId, 200_000, mmdBiotaIds: [], Guid.NewGuid());
        var taskB = boundaryB.ConvertPyrealDepositAsync(rawBiotaIdB, ShardId, ownerId, 200_000, mmdBiotaIds: [], Guid.NewGuid());

        var results = await Task.WhenAll(taskA, taskB);

        // Exactly one commits with mmdBiotaIds: [] (whichever ran against a 0 remainder); the other
        // must observe the combined 400,000 total (needing exactly one MMD) and refuse rather than
        // silently drop 200,000 Pyreals.
        Assert.AreEqual(1, results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Committed));
        Assert.AreEqual(1, results.Count(r => r.Kind == CloudBoundaryOutcomeKind.Conflict));
    }
}
