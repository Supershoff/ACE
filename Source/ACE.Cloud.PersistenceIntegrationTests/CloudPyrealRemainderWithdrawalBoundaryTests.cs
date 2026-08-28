using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #14's raw Pyreal Remainder withdrawal (DEP-006): capacity failure
/// (insufficient remainder), retry (idempotent replay), successful delivery, and mismatched-tender
/// refusal.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudPyrealRemainderWithdrawalBoundaryTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 800_000;

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

    private async Task<Guid> DepositRemainderAsync(Microsoft.EntityFrameworkCore.DbContextOptions<CloudDbContext> options, Guid ownerId, long amount)
    {
        var rawBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, rawBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, rawBiotaId, amount);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var outcome = await boundary.ConvertPyrealDepositAsync(rawBiotaId, ShardId, ownerId, amount, mmdBiotaIds: [], Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        return ownerId;
    }

    [TestMethod]
    public async Task WithdrawPyrealRemainder_MoreThanAvailable_RefusesAndLeavesTheRemainderUnchanged()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        await DepositRemainderAsync(options, ownerId, 500);

        var deliveryBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, deliveryBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, deliveryBiotaId, 501);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var recipientContainerId = NextId();

        var outcome = await boundary.WithdrawPyrealRemainderAsync(ShardId, ownerId, 501, [deliveryBiotaId], recipientContainerId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);

        await using var verifyContext = new CloudDbContext(options);
        var remainder = await verifyContext.CloudPyrealRemainders.AsNoTracking().SingleAsync(r => r.OwnerId == ownerId);
        Assert.AreEqual(500, remainder.RemainderAmount, "A refused (capacity failure) withdrawal must leave the remainder exactly unchanged and retryable.");
        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, deliveryBiotaId));
    }

    [TestMethod]
    public async Task WithdrawPyrealRemainder_ExactlyTheAvailableAmount_DeliversItAndZeroesTheRemainder()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        await DepositRemainderAsync(options, ownerId, 12_345);

        var deliveryBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, deliveryBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, deliveryBiotaId, 12_345);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var recipientContainerId = NextId();

        var outcome = await boundary.WithdrawPyrealRemainderAsync(ShardId, ownerId, 12_345, [deliveryBiotaId], recipientContainerId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        Assert.AreEqual(0, outcome.Value!.NewRemainder);
        Assert.AreEqual(recipientContainerId, outcome.Value.RecipientContainerId);
        CollectionAssert.AreEqual(new[] { deliveryBiotaId }, outcome.Value.DeliveredBiotaIds.ToArray());

        Assert.IsTrue(await AceShardTestData.HasSpecificContainerAsync(_fixture.AceShardConnectionString, deliveryBiotaId, recipientContainerId));

        await using var verifyContext = new CloudDbContext(options);
        var remainder = await verifyContext.CloudPyrealRemainders.AsNoTracking().SingleAsync(r => r.OwnerId == ownerId);
        Assert.AreEqual(0, remainder.RemainderAmount);
    }

    [TestMethod]
    public async Task WithdrawPyrealRemainder_ADeliveryBiotaSumThatDoesNotMatchTheRequestedAmount_Refuses()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        await DepositRemainderAsync(options, ownerId, 1_000);

        var deliveryBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, deliveryBiotaId);
        // Deliberately supply a biota whose actual value does not match the requested amount, as if
        // ACE-side allocation drifted; this must never silently hand out the wrong amount.
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, deliveryBiotaId, 900);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.WithdrawPyrealRemainderAsync(ShardId, ownerId, 1_000, [deliveryBiotaId], NextId(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);

        await using var verifyContext = new CloudDbContext(options);
        var remainder = await verifyContext.CloudPyrealRemainders.AsNoTracking().SingleAsync(r => r.OwnerId == ownerId);
        Assert.AreEqual(1_000, remainder.RemainderAmount);
    }

    [TestMethod]
    public async Task WithdrawPyrealRemainder_RepeatedIdempotencyKey_ReplaysTheCommittedResultWithoutDeliveringTwice()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        await DepositRemainderAsync(options, ownerId, 5_000);

        var deliveryBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, deliveryBiotaId);
        await AceShardTestData.SetCoinValueAsync(_fixture.AceShardConnectionString, deliveryBiotaId, 5_000);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var recipientContainerId = NextId();
        var idempotencyKey = Guid.NewGuid();

        var first = await boundary.WithdrawPyrealRemainderAsync(ShardId, ownerId, 5_000, [deliveryBiotaId], recipientContainerId, idempotencyKey);
        var second = await boundary.WithdrawPyrealRemainderAsync(ShardId, ownerId, 5_000, [deliveryBiotaId], recipientContainerId, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind);
        Assert.AreEqual(0, first.Value!.NewRemainder);
        Assert.AreEqual(0, second.Value!.NewRemainder);

        Assert.AreEqual(1, await AceShardTestData.CountContainerRowsAsync(_fixture.AceShardConnectionString, deliveryBiotaId));
    }
}
