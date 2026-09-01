using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// AC Cloud Mule issue #38 Red -&gt; Green tests for VAULT-005's audited administrator recovery of an
/// out-of-band monarch deletion (ADM-002): "An out-of-band monarch deletion leaves the vault
/// available only for audited administrator recovery" and never guesses a successor. These exercise
/// <see cref="CloudMonarchVaultRecoveryGateway"/> against a diagnosed
/// <see cref="CloudMonarchDeletionDiagnostic"/>, produced the same way
/// <see cref="CloudMonarchVaultOrphanDetectionTests"/> already proves.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudMonarchVaultRecoveryGatewayTests
{
    private const string ShardId = "us1";
    private const uint AdminAccessLevel = 5;
    private const uint AdminAccountId = 999;

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 780_000;

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
    public async Task RecoverAsync_MovesWholeItemsAndStackLots_ToTheAdminChosenDestination()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, wholeItemBiotaId, stackBiotaId) = await SeedDiagnosedOrphanedVaultAsync(options);
        var destinationAccountId = NextId();

        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.RecoverAsync(
            ShardId, diagnostic.Id, AdminAccountId, destinationAccountId, destinationAccountExists: true,
            "Monarch deleted directly in the database; sending to the designated successor.", confirmed: true);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.AreEqual(1, outcome.Value!.CustodyRecordsMoved);
        Assert.AreEqual(1, outcome.Value!.StackLotsMoved);

        var destinationOwnerId = CloudOwnerIdentity.ForAccount(ShardId, destinationAccountId);
        Assert.AreEqual(destinationOwnerId, outcome.Value.DestinationOwnerId);

        var custodyRecord = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == wholeItemBiotaId);
        Assert.AreEqual(destinationOwnerId, custodyRecord.OwnerId);

        var lot = await context.CloudStackLots.AsNoTracking().SingleAsync(l => l.CustodyRecordId != custodyRecord.Id);
        Assert.AreEqual(destinationOwnerId, lot.OwnerId);

        var persistedDiagnostic = await context.CloudMonarchDeletionDiagnostics.AsNoTracking().SingleAsync(d => d.Id == diagnostic.Id);
        Assert.IsTrue(persistedDiagnostic.IsResolved);
        Assert.AreEqual(AdminAccountId, persistedDiagnostic.ResolvedByAdminAccountId);
        Assert.AreEqual(destinationOwnerId, persistedDiagnostic.DestinationOwnerId);
        Assert.IsNotNull(persistedDiagnostic.ResolvedAtUtc);
    }

    [TestMethod]
    public async Task RecoverAsync_MovesEveryItem_AppendsActivityLedgerAndCustodyOutboxEvents_ForProvenance()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, wholeItemBiotaId, stackBiotaId) = await SeedDiagnosedOrphanedVaultAsync(options);
        var destinationAccountId = NextId();
        var destinationOwnerId = CloudOwnerIdentity.ForAccount(ShardId, destinationAccountId);

        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.RecoverAsync(
            ShardId, diagnostic.Id, AdminAccountId, destinationAccountId, destinationAccountExists: true,
            "Audited recovery reason.", confirmed: true);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        var ledgerEvents = await context.CloudActivityLedgerEvents.AsNoTracking()
            .Where(e => e.EventType == CloudBoundaryOperationType.AdminVaultRecovery && e.OwnerId == destinationOwnerId)
            .ToListAsync();
        var outboxEvents = await context.CloudCustodyOutboxEvents.AsNoTracking()
            .Where(e => e.EventType == CloudBoundaryOperationType.AdminVaultRecovery && e.OwnerId == destinationOwnerId)
            .ToListAsync();

        var expectedBiotaIds = new[] { wholeItemBiotaId, stackBiotaId };

        CollectionAssert.AreEquivalent(expectedBiotaIds, ledgerEvents.Select(e => e.BiotaId).ToList());
        CollectionAssert.AreEquivalent(expectedBiotaIds, outboxEvents.Select(e => e.BiotaId).ToList());
        Assert.IsTrue(ledgerEvents.All(e => e.Reason == "Audited recovery reason."), "The administrator's own written reason must be preserved in the Activity Ledger (ADM-002).");
    }

    [TestMethod]
    public async Task RecoverAsync_NotifiesTheDestinationOwner()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, _, _) = await SeedDiagnosedOrphanedVaultAsync(options);
        var destinationAccountId = NextId();
        var destinationOwnerId = CloudOwnerIdentity.ForAccount(ShardId, destinationAccountId);

        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.RecoverAsync(
            ShardId, diagnostic.Id, AdminAccountId, destinationAccountId, destinationAccountExists: true,
            "Audited recovery reason.", confirmed: true);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        var notification = await context.CloudNotifications.AsNoTracking()
            .SingleOrDefaultAsync(n => n.ShardId == ShardId && n.OwnerId == destinationOwnerId && n.Kind == CloudNotificationKind.AdminVaultRecoveryApplied);

        Assert.IsNotNull(notification, "ADM-002: 'Affected owners receive the administrator's intervention reason in an in-app notification.'");
    }

    [TestMethod]
    public async Task RecoverAsync_DestinationAccountDoesNotExist_IsAConflict_AndDoesNotResolveOrMoveAnything()
    {
        // VAULT-005/ADM-002: a resolved diagnostic can never be re-applied, so an administrator typo
        // in the destination account must be refused now rather than permanently stranding the
        // vault's contents on an owner identity with no real account behind it.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, wholeItemBiotaId, _) = await SeedDiagnosedOrphanedVaultAsync(options);

        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.RecoverAsync(
            ShardId, diagnostic.Id, AdminAccountId, NextId(), destinationAccountExists: false,
            "A real reason.", confirmed: true);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);

        var persistedDiagnostic = await context.CloudMonarchDeletionDiagnostics.AsNoTracking().SingleAsync(d => d.Id == diagnostic.Id);
        Assert.IsFalse(persistedDiagnostic.IsResolved, "A recovery to a nonexistent destination account must never be committed.");

        var custodyRecord = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == wholeItemBiotaId);
        Assert.AreEqual(diagnostic.VaultOwnerId, custodyRecord.OwnerId, "A refused recovery must not move anything.");
    }

    [TestMethod]
    public async Task RecoverAsync_WithoutAWrittenReason_IsAConflict_AndDoesNotResolveOrMoveAnything()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, wholeItemBiotaId, _) = await SeedDiagnosedOrphanedVaultAsync(options);

        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.RecoverAsync(ShardId, diagnostic.Id, AdminAccountId, NextId(), destinationAccountExists: true, reason: null, confirmed: true);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);

        var persistedDiagnostic = await context.CloudMonarchDeletionDiagnostics.AsNoTracking().SingleAsync(d => d.Id == diagnostic.Id);
        Assert.IsFalse(persistedDiagnostic.IsResolved);

        var custodyRecord = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == wholeItemBiotaId);
        Assert.AreEqual(diagnostic.VaultOwnerId, custodyRecord.OwnerId, "A refused recovery must not move anything.");
    }

    [TestMethod]
    public async Task RecoverAsync_WithoutConfirmation_IsAConflict_AndDoesNotResolve()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, _, _) = await SeedDiagnosedOrphanedVaultAsync(options);

        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.RecoverAsync(ShardId, diagnostic.Id, AdminAccountId, NextId(), destinationAccountExists: true, "A real reason.", confirmed: false);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);

        var persistedDiagnostic = await context.CloudMonarchDeletionDiagnostics.AsNoTracking().SingleAsync(d => d.Id == diagnostic.Id);
        Assert.IsFalse(persistedDiagnostic.IsResolved);
    }

    [TestMethod]
    public async Task RecoverAsync_UnknownDiagnosticId_IsAConflict()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.RecoverAsync(ShardId, Guid.NewGuid(), AdminAccountId, NextId(), destinationAccountExists: true, "A real reason.", confirmed: true);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task RecoverAsync_ACommittedRecovery_CanNeverBeOverriddenByASecondAttempt()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, wholeItemBiotaId, _) = await SeedDiagnosedOrphanedVaultAsync(options);
        var firstDestinationAccountId = NextId();
        var firstDestinationOwnerId = CloudOwnerIdentity.ForAccount(ShardId, firstDestinationAccountId);

        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var firstOutcome = await gateway.RecoverAsync(
            ShardId, diagnostic.Id, AdminAccountId, firstDestinationAccountId, destinationAccountExists: true,
            "First administrator decision.", confirmed: true);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, firstOutcome.Kind, firstOutcome.Reason);

        // A retried/duplicate request -- proving both "retry/crash" safety and that a committed
        // transfer can never be overridden by a later attempt with a different destination.
        var secondDestinationAccountId = NextId();
        var secondOutcome = await gateway.RecoverAsync(
            ShardId, diagnostic.Id, AdminAccountId, secondDestinationAccountId, destinationAccountExists: true,
            "A different, later reason.", confirmed: true);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, secondOutcome.Kind);

        var custodyRecord = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == wholeItemBiotaId);
        Assert.AreEqual(firstDestinationOwnerId, custodyRecord.OwnerId, "The second attempt must never move the already-recovered item again.");

        var persistedDiagnostic = await context.CloudMonarchDeletionDiagnostics.AsNoTracking().SingleAsync(d => d.Id == diagnostic.Id);
        Assert.AreEqual("First administrator decision.", persistedDiagnostic.ResolutionReason);
        Assert.AreEqual(firstDestinationOwnerId, persistedDiagnostic.DestinationOwnerId);
    }

    [TestMethod]
    public async Task RecoverAsync_ConcurrentDuplicateRequests_OnlyOneCommits_ProvingTheRowLockSerializesThem()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, wholeItemBiotaId, _) = await SeedDiagnosedOrphanedVaultAsync(options);
        var destinationAccountIdA = NextId();
        var destinationAccountIdB = NextId();

        await using var contextA = new CloudDbContext(options);
        await using var contextB = new CloudDbContext(options);
        var gatewayA = new CloudMonarchVaultRecoveryGateway(contextA, new CloudAccountLinkGateway(contextA));
        var gatewayB = new CloudMonarchVaultRecoveryGateway(contextB, new CloudAccountLinkGateway(contextB));

        var taskA = gatewayA.RecoverAsync(ShardId, diagnostic.Id, AdminAccountId, destinationAccountIdA, destinationAccountExists: true, "Concurrent attempt A.", confirmed: true);
        var taskB = gatewayB.RecoverAsync(ShardId, diagnostic.Id, AdminAccountId, destinationAccountIdB, destinationAccountExists: true, "Concurrent attempt B.", confirmed: true);
        var results = await Task.WhenAll(taskA, taskB);

        Assert.HasCount(1, results.Where(r => r.Kind == CloudBoundaryOutcomeKind.Committed), "Exactly one concurrent recovery attempt for the same diagnostic must commit.");
        Assert.HasCount(1, results.Where(r => r.Kind == CloudBoundaryOutcomeKind.Conflict), "The other concurrent attempt must be refused as a conflict, never silently move the vault a second time.");

        await using var verifyContext = new CloudDbContext(options);
        var custodyRecord = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == wholeItemBiotaId);
        var winningDestinationOwnerId = results.Single(r => r.Kind == CloudBoundaryOutcomeKind.Committed).Value!.DestinationOwnerId;
        Assert.AreEqual(winningDestinationOwnerId, custodyRecord.OwnerId);
    }

    [TestMethod]
    public async Task WhileFrozen_RecoverAsync_IsRefused()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, _, _) = await SeedDiagnosedOrphanedVaultAsync(options);

        await using (var maintenanceContext = new CloudDbContext(options))
        {
            var maintenanceBoundary = new CloudGlobalMaintenanceBoundary(maintenanceContext);
            var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
            Assert.AreEqual(
                CloudBoundaryOutcomeKind.Committed,
                (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, AdminAccessLevel, AdminAccountId, initial.Version.Value)).Kind);
        }

        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.RecoverAsync(ShardId, diagnostic.Id, AdminAccountId, NextId(), destinationAccountExists: true, "A real reason.", confirmed: true);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "frozen");
    }

    [TestMethod]
    public async Task RecoverAsync_NeverTouchesAnUnrelatedDiagnosedVault_RegardlessOfHowManyOtherOnesExist()
    {
        // VAULT-005's "do not guess a successor": recovering one specific diagnostic must never be
        // influenced by -- or move anything belonging to -- any other diagnosed or ordinary
        // character/vault on the shard, whether there are zero, one, or several of them.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var (monarchId, diagnostic, wholeItemBiotaId, _) = await SeedDiagnosedOrphanedVaultAsync(options);
        var (_, otherDiagnostic, otherWholeItemBiotaId, _) = await SeedDiagnosedOrphanedVaultAsync(options);

        var unrelatedCharacterId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, unrelatedCharacterId, accountId: 1, name: "Unrelated");

        var destinationAccountId = NextId();
        var destinationOwnerId = CloudOwnerIdentity.ForAccount(ShardId, destinationAccountId);

        await using var context = new CloudDbContext(options);
        var gateway = new CloudMonarchVaultRecoveryGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.RecoverAsync(
            ShardId, diagnostic.Id, AdminAccountId, destinationAccountId, destinationAccountExists: true,
            "Recovering only the named diagnostic.", confirmed: true);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        var recoveredRecord = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == wholeItemBiotaId);
        Assert.AreEqual(destinationOwnerId, recoveredRecord.OwnerId);

        var untouchedRecord = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == otherWholeItemBiotaId);
        Assert.AreEqual(otherDiagnostic.VaultOwnerId, untouchedRecord.OwnerId, "An unrelated diagnosed vault must never be touched by recovering a different one.");

        var otherPersistedDiagnostic = await context.CloudMonarchDeletionDiagnostics.AsNoTracking().SingleAsync(d => d.Id == otherDiagnostic.Id);
        Assert.IsFalse(otherPersistedDiagnostic.IsResolved, "Recovering one diagnostic must never resolve an unrelated one.");
    }

    /// <summary>
    /// Deposits a whole item and a stack lot into a fresh monarch's Allegiance Vault, then produces
    /// exactly the out-of-band deletion diagnostic <see cref="CloudMonarchVaultOrphanDetectionTests"/>
    /// already proves: the character row disappears without ever routing through ACE's own guarded
    /// deletion path.
    /// </summary>
    private static async Task<(uint MonarchId, CloudMonarchDeletionDiagnostic Diagnostic, uint WholeItemBiotaId, uint StackBiotaId)> SeedDiagnosedOrphanedVaultAsync(
        DbContextOptions<CloudDbContext> options)
    {
        var monarchId = NextId();
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, monarchId, accountId: 1, name: $"OldMonarch{monarchId}");

        var wholeItemBiotaId = NextId();
        var stackBiotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, wholeItemBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, stackBiotaId);

        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var vaultGateway = new CloudAllegianceVaultGateway(context);
            await boundary.DepositAsync(wholeItemBiotaId, ShardId, vaultOwnerId, Guid.NewGuid());
            await boundary.DepositStackAsync(stackBiotaId, ShardId, vaultOwnerId, quantity: 10, Guid.NewGuid());

            // Establishes the reverse-lookup binding, exactly as an earlier deletion attempt or the
            // startup integrity scan would have before the out-of-band deletion below.
            await vaultGateway.GetIsEmptyAsync(ShardId, monarchId);
        }

        await AceShardTestData.DeleteCharacterRowAsync(_fixture.AceShardConnectionString, monarchId);

        await using (var context = new CloudDbContext(options))
        {
            var vaultGateway = new CloudAllegianceVaultGateway(context);
            var diagnostics = await vaultGateway.DetectOutOfBandMonarchVaultOrphansAsync(ShardId);
            var diagnostic = diagnostics.Single(d => d.MonarchCharacterId == monarchId);
            return (monarchId, diagnostic, wholeItemBiotaId, stackBiotaId);
        }
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
