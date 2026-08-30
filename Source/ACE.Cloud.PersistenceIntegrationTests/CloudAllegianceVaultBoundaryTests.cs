using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #17's Allegiance Vault emptiness check and Vault Absorption
/// (VAULT-001, VAULT-004, VAULT-005). A vault is modeled as an ordinary Cloud ownership identity
/// (<see cref="CloudOwnerIdentity.ForAllegianceVault"/>), so these tests exercise
/// <see cref="CloudAllegianceVaultGateway"/> through the exact same
/// <see cref="CloudCustodyBoundary"/> deposit/lot machinery every other owner uses.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudAllegianceVaultBoundaryTests
{
    private const string ShardId = "us1";
    private const uint AdminAccessLevel = 5;
    private const uint AdminAccountId = 999;

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 750_000;

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
    public async Task GetIsEmptyAsync_ForAMonarchWithNoActivity_IsTrue()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        var isEmpty = await vaultGateway.GetIsEmptyAsync(ShardId, NextId());

        Assert.IsTrue(isEmpty);
    }

    [TestMethod]
    public async Task GetIsEmptyAsync_AfterAWholeItemDeposit_IsFalse()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        var deposit = await boundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, deposit.Kind);

        var isEmpty = await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        Assert.IsFalse(isEmpty);
    }

    [TestMethod]
    public async Task GetIsEmptyAsync_AfterAStackLotOwnedByTheVault_IsFalse()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        var deposit = await boundary.DepositStackAsync(biotaId, ShardId, vaultOwnerId, quantity: 5, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, deposit.Kind);

        var isEmpty = await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        Assert.IsFalse(isEmpty);
    }

    [TestMethod]
    public async Task GetIsEmptyAsync_CreatesAReverseLookupBinding_EvenWhenEmpty()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();

        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);

        var binding = await context.CloudAllegianceVaultBindings.AsNoTracking()
            .SingleOrDefaultAsync(b => b.ShardId == ShardId && b.MonarchCharacterId == monarchId);

        Assert.IsNotNull(binding, "A vault's reverse-lookup binding must exist once its emptiness has ever been checked, so a later integrity scan can find it even if it never held anything.");
    }

    [TestMethod]
    public async Task AbsorbAsync_MovesWholeItemsAndStackLots_FromSourceToDestination()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var oldMonarchId = NextId();
        var newMonarchId = NextId();
        var oldVaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, oldMonarchId);
        var newVaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, newMonarchId);

        var wholeItemBiotaId = NextId();
        var stackBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, wholeItemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await boundary.DepositAsync(wholeItemBiotaId, ShardId, oldVaultOwnerId, Guid.NewGuid());
        await boundary.DepositStackAsync(stackBiotaId, ShardId, oldVaultOwnerId, quantity: 10, Guid.NewGuid());

        var outcome = await vaultGateway.AbsorbAsync(ShardId, oldMonarchId, newMonarchId);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.AreEqual(1, outcome.Value!.CustodyRecordsMoved);
        Assert.AreEqual(1, outcome.Value!.StackLotsMoved);

        Assert.IsTrue(await vaultGateway.GetIsEmptyAsync(ShardId, oldMonarchId), "The former monarch's vault must be empty after absorption.");
        Assert.IsFalse(await vaultGateway.GetIsEmptyAsync(ShardId, newMonarchId), "The new monarch's vault must now hold the absorbed contents.");

        var custodyRecord = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == wholeItemBiotaId);
        Assert.AreEqual(newVaultOwnerId, custodyRecord.OwnerId);

        var lot = await context.CloudStackLots.AsNoTracking().SingleAsync();
        Assert.AreEqual(newVaultOwnerId, lot.OwnerId);
    }

    /// <summary>
    /// Red -&gt; Green regression test for issue #17's review, finding 2 (P1): a successful Vault
    /// Absorption reassigned ownership directly and committed, with no Activity Ledger entry and no
    /// Custody Outbox event -- unlike every other Cloud ownership transfer this codebase performs
    /// (compare <see cref="CloudCustodyBoundary"/>'s deposit/withdrawal family, which always appends
    /// both in the same transaction). Without an outbox event, the companion web's read model --
    /// rebuilt purely by replaying the Custody Outbox -- silently diverges from actual ownership after
    /// every successful Absorption: the absorbed items still appear owned by the old vault identity in
    /// any projection built from outbox replay alone.
    /// </summary>
    [TestMethod]
    public async Task AbsorbAsync_MovesWholeItemsAndStackLots_AppendsActivityLedgerAndCustodyOutboxEvents()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var oldMonarchId = NextId();
        var newMonarchId = NextId();
        var oldVaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, oldMonarchId);
        var newVaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, newMonarchId);

        var wholeItemBiotaId = NextId();
        var stackBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, wholeItemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);
        var vaultGateway = new CloudAllegianceVaultGateway(context);
        await boundary.DepositAsync(wholeItemBiotaId, ShardId, oldVaultOwnerId, Guid.NewGuid());
        await boundary.DepositStackAsync(stackBiotaId, ShardId, oldVaultOwnerId, quantity: 10, Guid.NewGuid());

        var outcome = await vaultGateway.AbsorbAsync(ShardId, oldMonarchId, newMonarchId);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        var ledgerEvents = await context.CloudActivityLedgerEvents.AsNoTracking()
            .Where(e => e.EventType == CloudBoundaryOperationType.VaultAbsorption && e.OwnerId == newVaultOwnerId)
            .ToListAsync();
        var outboxEvents = await context.CloudCustodyOutboxEvents.AsNoTracking()
            .Where(e => e.EventType == CloudBoundaryOperationType.VaultAbsorption && e.OwnerId == newVaultOwnerId)
            .ToListAsync();

        var expectedBiotaIds = new[] { wholeItemBiotaId, stackBiotaId };

        CollectionAssert.AreEquivalent(
            expectedBiotaIds,
            ledgerEvents.Select(e => e.BiotaId).ToList(),
            "A successful Vault Absorption must append an Activity Ledger entry for every moved item, "
                + "recording provenance (CONTEXT.md: 'preserves each item's provenance ... in history').");

        CollectionAssert.AreEquivalent(
            expectedBiotaIds,
            outboxEvents.Select(e => e.BiotaId).ToList(),
            "A successful Vault Absorption must append a Custody Outbox event for every moved item, or the "
                + "companion web's read model silently diverges from actual ownership after every Absorption.");
    }

    [TestMethod]
    public async Task AbsorbAsync_OnAnAlreadyEmptySourceVault_SucceedsAsANoOp()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        var outcome = await vaultGateway.AbsorbAsync(ShardId, NextId(), NextId());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.AreEqual(0, outcome.Value!.TotalItemsMoved);
    }

    [TestMethod]
    public async Task AbsorbAsync_WithTheSameMonarchAsSourceAndDestination_IsAConflict()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();

        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        var outcome = await vaultGateway.AbsorbAsync(ShardId, monarchId, monarchId);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task AbsorbAsync_WhenRefused_RecordsADurableDiagnostic_InsteadOfOnlyALogLine()
    {
        // Issue #17 review, finding 2 (P1): before this fix, a refused/failed Vault Absorption left
        // only a log line -- not queryable, not part of the Activity Ledger, and invisible to the
        // out-of-band orphan scan. Any non-Committed AbsorbAsync outcome must now leave a durable,
        // admin-visible CloudMonarchDeletionDiagnostic keyed on the former monarch's vault identity.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        var outcome = await vaultGateway.AbsorbAsync(ShardId, monarchId, monarchId);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);

        var diagnostic = await context.CloudMonarchDeletionDiagnostics.AsNoTracking()
            .SingleOrDefaultAsync(d => d.MonarchCharacterId == monarchId);

        Assert.IsNotNull(diagnostic, "A refused Vault Absorption must leave a durable, queryable diagnostic, not only a log line.");
        Assert.AreEqual(vaultOwnerId, diagnostic.VaultOwnerId);
    }

    [TestMethod]
    public async Task AbsorbAsync_WhenAlreadyDiagnosed_DoesNotRecordASecondDiagnostic()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();

        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        await vaultGateway.AbsorbAsync(ShardId, monarchId, monarchId);
        await vaultGateway.AbsorbAsync(ShardId, monarchId, monarchId);

        var diagnosticCount = await context.CloudMonarchDeletionDiagnostics.AsNoTracking()
            .CountAsync(d => d.MonarchCharacterId == monarchId);

        Assert.AreEqual(1, diagnosticCount, "A vault already diagnosed must never be diagnosed again.");
    }

    /// <summary>
    /// Red -&gt; Green regression test for issue #23's review [P1]: this PR switched
    /// <see cref="CloudAllegianceVaultGateway"/> from a hardcoded <see cref="CloudMutationGateState.Open"/>
    /// to the real resolved gate, but no test proved Global Cloud Maintenance actually blocks
    /// <see cref="CloudAllegianceVaultGateway.AbsorbAsync"/>, unlike every <c>CloudCustodyBoundary</c>
    /// call site (each of which has its own <c>WhileFrozen_*</c> test).
    /// </summary>
    [TestMethod]
    public async Task WhileFrozen_AbsorbAsync_IsRefused_ProvingTheRealGateBlocksTheAbsorptionCallSite()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var oldMonarchId = NextId();
        var newMonarchId = NextId();

        await using (var maintenanceContext = new CloudDbContext(options))
        {
            var maintenanceBoundary = new CloudGlobalMaintenanceBoundary(maintenanceContext);
            var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
            Assert.AreEqual(
                CloudBoundaryOutcomeKind.Committed,
                (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);
        }

        await using var context = new CloudDbContext(options);
        var vaultGateway = new CloudAllegianceVaultGateway(context);

        var outcome = await vaultGateway.AbsorbAsync(ShardId, oldMonarchId, newMonarchId);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "frozen");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
