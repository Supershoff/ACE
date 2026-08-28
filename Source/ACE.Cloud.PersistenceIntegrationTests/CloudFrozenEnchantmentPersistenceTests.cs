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

    /// <summary>
    /// AC Cloud Mule issue #15 (DEP-005): while a biota is in Cloud custody, nothing in ACE calls
    /// <c>EnchantmentManager.HeartBeat</c> on it (its Container/Wielder/Location rows are gone, so it
    /// is never reachable from any ticking Container's Inventory_Tick loop), so ace_shard's own
    /// registry row for a Frozen Enchantment must stay byte-identical for as long as custody lasts --
    /// it neither ticks (start_Time decreasing) nor is removed.
    /// </summary>
    [TestMethod]
    public async Task Deposit_WithFrozenEnchantments_LeavesTheAceShardRegistryRowUntouchedWhileInCustody()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.InsertEnchantmentRegistryRowAsync(_fixture.AceShardConnectionString, biotaId, spellId: 777, startTime: -30.0, duration: 120.0);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var preservationRequirements = new[] { new CloudRuntimeEnchantmentSnapshot(spellId: 777, remainingDurationSeconds: 90.0) };
        var depositOutcome = await boundary.DepositAsync(
            biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid(), preservationRequirements: preservationRequirements);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind, depositOutcome.Reason);

        // Nothing calls HeartBeat while this biota has no world possession -- simulate an arbitrary
        // amount of wall/database time passing by simply re-reading the row well after the deposit
        // committed, with no intervening code path that could tick it.
        await Task.Delay(TimeSpan.FromMilliseconds(250));

        Assert.AreEqual(1, await AceShardTestData.CountEnchantmentRegistryRowsAsync(_fixture.AceShardConnectionString, biotaId, 777),
            "A Frozen Enchantment's native registry row must not be removed while its biota is in Cloud custody.");
        Assert.AreEqual(-30.0, await AceShardTestData.GetEnchantmentStartTimeAsync(_fixture.AceShardConnectionString, biotaId, 777),
            "A Frozen Enchantment's native registry row must not tick while its biota is in Cloud custody.");
    }

    /// <summary>
    /// AC Cloud Mule issue #15 (DEP-005): ACE's periodic autosave can leave ace_shard's own registry
    /// row behind the live in-memory remaining duration a deposit captures (`Player.BuildRuntimeEnchantments`'s
    /// doc comment: "must be able to resume heartbeat processing from the exact preserved remaining
    /// duration without re-deriving it from ace_shard"). Withdrawal must therefore overwrite
    /// ace_shard's start_Time with the exact captured remaining duration -- neither extending it (a
    /// stale, larger countdown) nor shortening it (a stale, smaller one) -- so ordinary ACE inventory
    /// ticking resumes correctly once the item is back in a Container.
    /// </summary>
    [TestMethod]
    public async Task Withdraw_AnItemDepositedWithFrozenEnchantments_ResumesTheExactPreservedRemainingDuration_OverwritingAStaleAceShardValue()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        // The ace_shard row was last autosaved with a much larger countdown than the exact live value
        // the deposit below captures -- modeling autosave lag between the two.
        await AceShardTestData.InsertEnchantmentRegistryRowAsync(_fixture.AceShardConnectionString, biotaId, spellId: 777, startTime: -999.0, duration: 1200.0);

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

        var startTime = await AceShardTestData.GetEnchantmentStartTimeAsync(_fixture.AceShardConnectionString, biotaId, 777);
        Assert.IsNotNull(startTime);
        // duration (1200, untouched since deposit) + start_Time must equal the exact preserved 45s,
        // matching EnchantmentManager's own "Duration + StartTime" remaining-duration arithmetic.
        Assert.AreEqual(45.0, 1200.0 + startTime!.Value, 0.0001,
            "Withdrawal must resume from the exact preserved remaining duration, not a stale ace_shard value.");
    }

    [TestMethod]
    public async Task Withdraw_AnItemDepositedWithMultipleFrozenEnchantments_ResumesEachSpellsExactRemainingDuration()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.InsertEnchantmentRegistryRowAsync(_fixture.AceShardConnectionString, biotaId, spellId: 1234, startTime: -600.0, duration: 900.0);
        await AceShardTestData.InsertEnchantmentRegistryRowAsync(_fixture.AceShardConnectionString, biotaId, spellId: 5678, startTime: -5.0, duration: 30.0);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var recipientContainerId = NextBiotaId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var preservationRequirements = new[]
        {
            new CloudRuntimeEnchantmentSnapshot(spellId: 1234, remainingDurationSeconds: 90.5),
            new CloudRuntimeEnchantmentSnapshot(spellId: 5678, remainingDurationSeconds: 12.0),
        };

        var depositOutcome = await boundary.DepositAsync(
            biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid(), preservationRequirements: preservationRequirements);
        var custodyRecordId = depositOutcome.Value!.Id;

        var withdrawOutcome = await boundary.WithdrawAsync(custodyRecordId, expectedVersion: 1, recipientContainerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, withdrawOutcome.Kind, withdrawOutcome.Reason);

        var firstStartTime = await AceShardTestData.GetEnchantmentStartTimeAsync(_fixture.AceShardConnectionString, biotaId, 1234);
        var secondStartTime = await AceShardTestData.GetEnchantmentStartTimeAsync(_fixture.AceShardConnectionString, biotaId, 5678);
        Assert.AreEqual(90.5, 900.0 + firstStartTime!.Value, 0.0001);
        Assert.AreEqual(12.0, 30.0 + secondStartTime!.Value, 0.0001);
    }

    /// <summary>
    /// AC Cloud Mule issue #15 review, P1: <c>biota_properties_enchantment_registry</c>'s real
    /// identity is (object_Id, spell_Id, layer_Id) -- <c>EnchantmentManager.Add</c> assigns
    /// successive LayerIds to multiple layers of the same spell on the same object (e.g. two
    /// different casters' independent DoTs of the same spell). Resuming by spell_Id alone would let
    /// whichever layer's UPDATE runs last overwrite every other layer sharing that spell_Id, silently
    /// corrupting or duplicating one layer's remaining duration -- this proves each layer resumes
    /// independently to its own exact preserved value.
    /// </summary>
    [TestMethod]
    public async Task Withdraw_AnItemDepositedWithTwoLayersOfTheSameSpell_ResumesEachLayersOwnExactRemainingDuration()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.InsertEnchantmentRegistryRowAsync(
            _fixture.AceShardConnectionString, biotaId, spellId: 500, startTime: -30.0, duration: 60.0, layerId: 1);
        await AceShardTestData.InsertEnchantmentRegistryRowAsync(
            _fixture.AceShardConnectionString, biotaId, spellId: 500, startTime: -40.0, duration: 90.0, layerId: 2);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var recipientContainerId = NextBiotaId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var preservationRequirements = new[]
        {
            new CloudRuntimeEnchantmentSnapshot(spellId: 500, remainingDurationSeconds: 30.0, layerId: 1),
            new CloudRuntimeEnchantmentSnapshot(spellId: 500, remainingDurationSeconds: 50.0, layerId: 2),
        };

        var depositOutcome = await boundary.DepositAsync(
            biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid(), preservationRequirements: preservationRequirements);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind, depositOutcome.Reason);
        var custodyRecordId = depositOutcome.Value!.Id;

        var withdrawOutcome = await boundary.WithdrawAsync(custodyRecordId, expectedVersion: 1, recipientContainerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, withdrawOutcome.Kind, withdrawOutcome.Reason);

        var firstLayerStartTime = await AceShardTestData.GetEnchantmentStartTimeAsync(_fixture.AceShardConnectionString, biotaId, 500, layerId: 1);
        var secondLayerStartTime = await AceShardTestData.GetEnchantmentStartTimeAsync(_fixture.AceShardConnectionString, biotaId, 500, layerId: 2);
        Assert.IsNotNull(firstLayerStartTime);
        Assert.IsNotNull(secondLayerStartTime);
        Assert.AreEqual(30.0, 60.0 + firstLayerStartTime!.Value, 0.0001,
            "Layer 1 must resume from its own exact preserved remaining duration, unaffected by layer 2's resume.");
        Assert.AreEqual(50.0, 90.0 + secondLayerStartTime!.Value, 0.0001,
            "Layer 2 must resume from its own exact preserved remaining duration, unaffected by layer 1's resume.");
    }

    [TestMethod]
    public async Task Withdraw_AnItemWithAPermanentBuiltInSpell_LeavesItsRegistryRowCompletelyUnaffected()
    {
        // DEP-005: "Permanent built-in spells remain ordinary static properties." A permanent
        // equip-linked spell (Duration == -1) never becomes a CloudFrozenEnchantment
        // (Player.BuildRuntimeEnchantments excludes it), so withdrawal's resume logic must never
        // touch its registry row.
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.InsertEnchantmentRegistryRowAsync(_fixture.AceShardConnectionString, biotaId, spellId: 999, startTime: 0.0, duration: -1.0);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var recipientContainerId = NextBiotaId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var depositOutcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind, depositOutcome.Reason);
        var custodyRecordId = depositOutcome.Value!.Id;

        var withdrawOutcome = await boundary.WithdrawAsync(custodyRecordId, expectedVersion: 1, recipientContainerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, withdrawOutcome.Kind, withdrawOutcome.Reason);

        Assert.AreEqual(1, await AceShardTestData.CountEnchantmentRegistryRowsAsync(_fixture.AceShardConnectionString, biotaId, 999));
        Assert.AreEqual(0.0, await AceShardTestData.GetEnchantmentStartTimeAsync(_fixture.AceShardConnectionString, biotaId, 999));
    }

    [TestMethod]
    public async Task RedeemWithdrawalReservation_AnItemDepositedWithFrozenEnchantments_ResumesTheExactPreservedRemainingDuration()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.InsertEnchantmentRegistryRowAsync(_fixture.AceShardConnectionString, biotaId, spellId: 777, startTime: -999.0, duration: 1200.0);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();
        var tokenHash = Convert.ToHexString(Guid.NewGuid().ToByteArray());
        var recipientContainerId = NextBiotaId();

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var preservationRequirements = new[] { new CloudRuntimeEnchantmentSnapshot(spellId: 777, remainingDurationSeconds: 45.0) };

        var depositOutcome = await boundary.DepositAsync(
            biotaId, ShardId, ownerId, Guid.NewGuid(), preservationRequirements: preservationRequirements);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, depositOutcome.Kind, depositOutcome.Reason);

        var reserveOutcome = await boundary.ReserveForWithdrawalAsync(
            biotaId, ShardId, ownerId, tokenHash, TimeSpan.FromMinutes(15), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);

        var redeemOutcome = await boundary.RedeemWithdrawalReservationAsync(tokenHash, recipientContainerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, redeemOutcome.Kind, redeemOutcome.Reason);

        var startTime = await AceShardTestData.GetEnchantmentStartTimeAsync(_fixture.AceShardConnectionString, biotaId, 777);
        Assert.IsNotNull(startTime);
        Assert.AreEqual(45.0, 1200.0 + startTime!.Value, 0.0001,
            "Redeeming a Withdrawal Reservation must resume from the exact preserved remaining duration, not a stale ace_shard value.");
    }
}
