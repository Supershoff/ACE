using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #13's DEP-005 gap: a deposit's Frozen Enchantment preservation
/// requirements must actually persist as <see cref="CloudFrozenEnchantment"/> rows tied to the
/// created <see cref="CloudCustodyRecord"/>, in the same transaction as the custody transition
/// (transaction rule 5), rather than being silently discarded.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudFrozenEnchantmentPersistenceTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 940_000;

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

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);

    [TestMethod]
    public async Task Deposit_WithPreservationRequirements_PersistsFrozenEnchantmentsTiedToTheCustodyRecord()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var preservationRequirements = new[]
        {
            new CloudRuntimeEnchantmentSnapshot(spellId: 1234, remainingDurationSeconds: 90.5),
            new CloudRuntimeEnchantmentSnapshot(spellId: 5678, remainingDurationSeconds: 12.0),
        };

        var outcome = await boundary.DepositAsync(
            biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid(), preservationRequirements: preservationRequirements);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        await using var verifyContext = new CloudDbContext(options);
        var frozen = await verifyContext.CloudFrozenEnchantments
            .Where(f => f.CustodyRecordId == outcome.Value!.Id)
            .OrderBy(f => f.SpellId)
            .ToListAsync();

        Assert.HasCount(2, frozen);
        Assert.AreEqual(1234, frozen[0].SpellId);
        Assert.AreEqual(90.5, frozen[0].RemainingDurationSeconds);
        Assert.AreEqual(5678, frozen[1].SpellId);
        Assert.AreEqual(12.0, frozen[1].RemainingDurationSeconds);
    }

    [TestMethod]
    public async Task Deposit_WithNoPreservationRequirements_PersistsNoFrozenEnchantments()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        await using var verifyContext = new CloudDbContext(options);
        var count = await verifyContext.CloudFrozenEnchantments.CountAsync(f => f.CustodyRecordId == outcome.Value!.Id);
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task DepositStack_WithPreservationRequirements_PersistsFrozenEnchantmentsTiedToTheStackCustodyRecord()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var preservationRequirements = new[] { new CloudRuntimeEnchantmentSnapshot(spellId: 42, remainingDurationSeconds: 30.0) };

        var outcome = await boundary.DepositStackAsync(
            biotaId, ShardId, Guid.NewGuid(), quantity: 15, Guid.NewGuid(), preservationRequirements: preservationRequirements);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        await using var verifyContext = new CloudDbContext(options);
        var frozen = await verifyContext.CloudFrozenEnchantments
            .SingleAsync(f => f.CustodyRecordId == outcome.Value!.CustodyRecord.Id);

        Assert.AreEqual(42, frozen.SpellId);
        Assert.AreEqual(30.0, frozen.RemainingDurationSeconds);
    }

    [TestMethod]
    public async Task Withdraw_AnItemDepositedWithFrozenEnchantments_SucceedsAndRemovesTheFrozenEnchantmentRows()
    {
        // Issue #13 review, finding 1: CloudFrozenEnchantment.CustodyRecordId's foreign key is
        // ON DELETE RESTRICT, so removing a CloudCustodyRecord that still has CloudFrozenEnchantment
        // rows attached must throw unless those rows are deleted first. Without that cleanup this
        // withdrawal throws a DbUpdateException instead of committing.
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var recipientContainerId = NextBiotaId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var preservationRequirements = new[] { new CloudRuntimeEnchantmentSnapshot(spellId: 777, remainingDurationSeconds: 45.0) };

        var depositOutcome = await boundary.DepositAsync(
            biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid(), preservationRequirements: preservationRequirements);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind, depositOutcome.Reason);
        var custodyRecordId = depositOutcome.Value!.Id;

        var withdrawOutcome = await boundary.WithdrawAsync(custodyRecordId, expectedVersion: 1, recipientContainerId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, withdrawOutcome.Kind, withdrawOutcome.Reason);

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudFrozenEnchantments.CountAsync(f => f.CustodyRecordId == custodyRecordId));
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.Id == custodyRecordId));
    }
}
