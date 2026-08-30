using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #23's INV-004/INV-005/INV-006 section: "Test unlimited defaults,
/// personal/vault projected-lot counts, lowered limits, reduce-only actions, incoming obligations, and
/// binding settlement above a new quota." Also proves the acceptance criterion "Quota reductions never
/// break binding obligations and leave over-limit recipients reduce-only."
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStorageQuotaLimitsBoundaryTests
{
    private const string ShardId = "us1";
    private const uint AdminAccessLevel = 5;
    private const uint NonAdminAccessLevel = 1;

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 970_000;

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

    private CloudStorageQuotaLimitsBoundary NewQuotaBoundary(out CloudDbContext context)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        context = new CloudDbContext(options);
        return new CloudStorageQuotaLimitsBoundary(context);
    }

    [TestMethod]
    public async Task GetCurrent_OnFirstEverRead_BootstrapsUnlimitedForBothScopes()
    {
        var boundary = NewQuotaBoundary(out var context);
        await using var _ = context;

        var limits = await boundary.GetCurrentAsync(ShardId);

        Assert.IsNull(limits.PersonalLimit);
        Assert.IsNull(limits.VaultLimit);
    }

    [TestMethod]
    public async Task SetPersonalLimit_ByAnAdmin_Succeeds_AndPersistsAcrossASimulatedRestart()
    {
        await using (var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var boundary = new CloudStorageQuotaLimitsBoundary(context);
            var initial = await boundary.GetCurrentAsync(ShardId);

            var outcome = await boundary.SetPersonalLimitAsync(ShardId, 2, AdminAccessLevel, initial.Version.Value);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
        }

        await using var restarted = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var restartedBoundary = new CloudStorageQuotaLimitsBoundary(restarted);
        var limits = await restartedBoundary.GetCurrentAsync(ShardId);

        Assert.AreEqual(2, limits.PersonalLimit);
    }

    [TestMethod]
    public async Task SetPersonalLimit_ByANonAdmin_IsRejected()
    {
        var boundary = NewQuotaBoundary(out var context);
        await using var _ = context;

        var initial = await boundary.GetCurrentAsync(ShardId);
        var outcome = await boundary.SetPersonalLimitAsync(ShardId, 2, NonAdminAccessLevel, initial.Version.Value);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task Deposit_WithNoQuotaConfigured_AlwaysSucceeds()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var outcome = await custodyBoundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
    }

    [TestMethod]
    public async Task Deposit_OnceThePersonalQuotaIsMet_IsRefused_LeavingTheOwnerReduceOnly()
    {
        var quotaBoundary = NewQuotaBoundary(out var quotaContext);
        await using var __ = quotaContext;
        var initialLimits = await quotaBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await quotaBoundary.SetPersonalLimitAsync(ShardId, 1, AdminAccessLevel, initialLimits.Version.Value)).Kind);

        var ownerId = Guid.NewGuid();
        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));

        var firstBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        var firstOutcome = await custodyBoundary.DepositAsync(firstBiotaId, ShardId, ownerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, firstOutcome.Kind, "The first deposit, at the limit, must still be permitted.");

        var secondBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);
        var secondOutcome = await custodyBoundary.DepositAsync(secondBiotaId, ShardId, ownerId, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, secondOutcome.Kind, "INV-005: a second deposit beyond the quota must be refused (reduce-only).");
        Assert.IsTrue(
            await AceShardTestData.BiotaExistsAsync(_fixture.AceShardConnectionString, secondBiotaId),
            "A refused deposit must never delete or otherwise touch the biota still in the world.");
    }

    [TestMethod]
    public async Task Deposit_ForADifferentOwner_IsUnaffectedByAnotherOwnersQuotaOccupancy()
    {
        var quotaBoundary = NewQuotaBoundary(out var quotaContext);
        await using var __ = quotaContext;
        var initialLimits = await quotaBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await quotaBoundary.SetPersonalLimitAsync(ShardId, 1, AdminAccessLevel, initialLimits.Version.Value)).Kind);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));

        var firstOwnerBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstOwnerBiotaId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await custodyBoundary.DepositAsync(firstOwnerBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid())).Kind);

        var secondOwnerBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondOwnerBiotaId);
        var secondOwnerOutcome = await custodyBoundary.DepositAsync(secondOwnerBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, secondOwnerOutcome.Kind);
    }

    [TestMethod]
    public async Task LoweringTheQuotaBelowAnOwnersExistingCount_NeverDeletesOrTransfersTheirAssets_ButRefusesFurtherDeposits()
    {
        var ownerId = Guid.NewGuid();
        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));

        var firstBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(firstBiotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        var secondBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(secondBiotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        // Now lower the quota to 1, below this owner's already-deposited count of 2.
        var quotaBoundary = NewQuotaBoundary(out var quotaContext);
        await using var _ = quotaContext;
        var initialLimits = await quotaBoundary.GetCurrentAsync(ShardId);
        var lowerOutcome = await quotaBoundary.SetPersonalLimitAsync(ShardId, 1, AdminAccessLevel, initialLimits.Version.Value);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, lowerOutcome.Kind, "INV-005: lowering a quota below an existing count must still succeed.");

        Assert.IsTrue(await AceShardTestData.BiotaExistsAsync(_fixture.AceShardConnectionString, firstBiotaId));
        Assert.IsTrue(await AceShardTestData.BiotaExistsAsync(_fixture.AceShardConnectionString, secondBiotaId));

        var thirdBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, thirdBiotaId);
        var thirdOutcome = await custodyBoundary.DepositAsync(thirdBiotaId, ShardId, ownerId, Guid.NewGuid());

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Conflict, thirdOutcome.Kind,
            "INV-005: the now over-limit owner must be reduce-only -- every further deposit refused.");
    }

    [TestMethod]
    public async Task SettlementViaOwnershipTransfer_AboveALoweredQuota_StillSucceeds_INV006Exemption()
    {
        // INV-006: quota is checked only when a new obligation is created/accepted; it must never
        // break an already-binding settlement. CloudOwnershipTransferAuthority represents exactly
        // that settlement step and must not be quota-gated, even once the recipient is already at (or
        // pushed over) a since-lowered personal limit.
        var recipientId = Guid.NewGuid();
        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));

        var alreadyOwnedBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, alreadyOwnedBiotaId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await custodyBoundary.DepositAsync(alreadyOwnedBiotaId, ShardId, recipientId, Guid.NewGuid())).Kind);

        var quotaBoundary = NewQuotaBoundary(out var quotaContext);
        await using var _ = quotaContext;
        var initialLimits = await quotaBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await quotaBoundary.SetPersonalLimitAsync(ShardId, 1, AdminAccessLevel, initialLimits.Version.Value)).Kind);

        var incomingBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, incomingBiotaId);
        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed,
            (await custodyBoundary.DepositAsync(incomingBiotaId, ShardId, Guid.NewGuid(), Guid.NewGuid())).Kind);

        var transferAuthority = new CloudOwnershipTransferAuthority(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var transferOutcome = await transferAuthority.TransferAsync(incomingBiotaId, recipientId, expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed, transferOutcome.Kind,
            "INV-006: settlement must complete even though the recipient is already at their personal quota.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
