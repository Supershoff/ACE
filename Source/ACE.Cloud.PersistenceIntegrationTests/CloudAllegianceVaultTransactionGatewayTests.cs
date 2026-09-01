using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #37's Red -&gt; Green tests against a real MariaDB (VAULT-001, VAULT-002, VAULT-003, WDR-007):
/// Acting Character resolution against live ace_shard state (not the versioned identity/allegiance
/// cache), unrelated alts, equal privileges for any current member, dead/deleted characters, Storage
/// Quota, idempotent replay, and every prohibited vault action.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudAllegianceVaultTransactionGatewayTests
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

    [TestMethod]
    public async Task ContributeAsync_AWholeItemOwnedByTheActingCharactersAccount_MovesItIntoTheirVault()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(biotaId, ShardId, personalOwnerId, Guid.NewGuid())).Kind);

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.ContributeAsync(
            ShardId, accountId, characterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var record = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(vaultOwnerId, record.OwnerId);

        var ledgerEvent = await context.CloudActivityLedgerEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.EventType == CloudBoundaryOperationType.VaultContribution && e.BiotaId == biotaId);
        Assert.IsNotNull(ledgerEvent, "A contribution must append an Activity Ledger entry (EVT-001).");

        var outboxEvent = await context.CloudCustodyOutboxEvents.AsNoTracking()
            .SingleOrDefaultAsync(e => e.EventType == CloudBoundaryOperationType.VaultContribution && e.BiotaId == biotaId);
        Assert.IsNotNull(outboxEvent, "A contribution must append a Custody Outbox event (ARCH-007).");
    }

    [TestMethod]
    public async Task ContributeAsync_AStackLot_MovesItIntoTheVault()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        var deposit = await custodyBoundary.DepositStackAsync(biotaId, ShardId, personalOwnerId, quantity: 10, Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, deposit.Kind, deposit.Reason);

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.ContributeAsync(
            ShardId, accountId, characterId, CloudReservationTarget.ForStackLot(new CloudStackLotId(deposit.Value!.Lot.Id)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var lot = await context.CloudStackLots.AsNoTracking().SingleAsync(l => l.Id == deposit.Value.Lot.Id);
        Assert.AreEqual(vaultOwnerId, lot.OwnerId);
    }

    [TestMethod]
    public async Task TakeAsync_AnItemInTheActingCharactersVault_MovesItIntoTheirPersonalInventoryAndNotifiesThem()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid())).Kind);

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.TakeAsync(
            ShardId, accountId, characterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var record = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(personalOwnerId, record.OwnerId);

        var notification = await context.CloudNotifications.AsNoTracking()
            .SingleOrDefaultAsync(n => n.OwnerId == personalOwnerId && n.Kind == CloudNotificationKind.OwnershipReceived);
        Assert.IsNotNull(notification, "A take must notify the Acting Character's own account (EVT-003).");
    }

    [TestMethod]
    public async Task ContributeAsync_RepeatedWithTheSameIdempotencyKey_ReplaysTheOriginalResultInsteadOfContributingTwice()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();
        var idempotencyKey = Guid.NewGuid();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, personalOwnerId, Guid.NewGuid());

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var target = CloudReservationTarget.ForItem(new CloudItemId(biotaId));

        var first = await gateway.ContributeAsync(ShardId, accountId, characterId, target, idempotencyKey);
        var second = await gateway.ContributeAsync(ShardId, accountId, characterId, target, idempotencyKey);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind, first.Reason);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind, second.Reason);

        var ledgerEventCount = await context.CloudActivityLedgerEvents.AsNoTracking()
            .CountAsync(e => e.EventType == CloudBoundaryOperationType.VaultContribution && e.BiotaId == biotaId);
        Assert.AreEqual(1, ledgerEventCount, "A repeated idempotency key must never contribute the same item twice.");
    }

    [TestMethod]
    public async Task ContributeAsync_ByACharacterWithNoCurrentAllegiance_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var biotaId = NextId();

        // No GrantMonarchAsync call, and this character has no vassals: a true unaffiliated character.
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Loner");
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, personalOwnerId, Guid.NewGuid());

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.ContributeAsync(
            ShardId, accountId, characterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "allegiance");
    }

    [TestMethod]
    public async Task ContributeAsync_ByADeletedActingCharacter_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Deleted", isDeleted: true);
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, personalOwnerId, Guid.NewGuid());

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.ContributeAsync(
            ShardId, accountId, characterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task ContributeAsync_ByACharacterThatDoesNotBelongToTheCallersAccountGroup_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var callerAccountId = NextId();
        var unrelatedAccountId = NextId();
        var unrelatedCharacterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, unrelatedCharacterId, unrelatedAccountId, "NotMine");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, unrelatedCharacterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var callerOwnerId = CloudOwnerIdentity.ForAccount(ShardId, callerAccountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, callerOwnerId, Guid.NewGuid());

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.ContributeAsync(
            ShardId, callerAccountId, unrelatedCharacterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "does not currently belong");
    }

    [TestMethod]
    public async Task TakeAsync_AnUnrelatedCharactersAllegianceVaultItem_IsRejected()
    {
        // Two different allegiances; the Acting Character (accountId/characterId, allegiance A) must
        // never be able to take from allegiance B's vault (VAULT-001: "one alt's membership never
        // grants unrelated characters authority").
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var ownMonarchId = NextId();
        var otherMonarchId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, ownMonarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var otherVaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, otherMonarchId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, otherVaultOwnerId, Guid.NewGuid());

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.TakeAsync(
            ShardId, accountId, characterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
    }

    [TestMethod]
    public async Task ContributeAndTake_TwoDifferentCurrentMembersOfTheSameAllegiance_HaveEqualPrivileges()
    {
        // VAULT-002: no rank ACLs -- two entirely different accounts/characters sworn to the same
        // monarch both freely contribute to and take from the one shared vault.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchId = NextId();
        var firstAccountId = NextId();
        var firstCharacterId = NextId();
        var secondAccountId = NextId();
        var secondCharacterId = NextId();
        var firstBiotaId = NextId();
        var secondBiotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, firstCharacterId, firstAccountId, "First");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, firstCharacterId, monarchId);
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, secondCharacterId, secondAccountId, "Second");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, secondCharacterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, firstBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, secondBiotaId);

        await using var context = new CloudDbContext(options);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(firstBiotaId, ShardId, CloudOwnerIdentity.ForAccount(ShardId, firstAccountId), Guid.NewGuid());
        await custodyBoundary.DepositAsync(secondBiotaId, ShardId, CloudOwnerIdentity.ForAccount(ShardId, secondAccountId), Guid.NewGuid());

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));

        var firstContribute = await gateway.ContributeAsync(
            ShardId, firstAccountId, firstCharacterId, CloudReservationTarget.ForItem(new CloudItemId(firstBiotaId)), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, firstContribute.Kind, firstContribute.Reason);

        // The second member takes the first member's contribution out of the shared vault.
        var secondTake = await gateway.TakeAsync(
            ShardId, secondAccountId, secondCharacterId, CloudReservationTarget.ForItem(new CloudItemId(firstBiotaId)), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, secondTake.Kind, secondTake.Reason);

        var secondContribute = await gateway.ContributeAsync(
            ShardId, secondAccountId, secondCharacterId, CloudReservationTarget.ForItem(new CloudItemId(secondBiotaId)), Guid.NewGuid());
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, secondContribute.Kind, secondContribute.Reason);

        var record = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == firstBiotaId);
        Assert.AreEqual(CloudOwnerIdentity.ForAccount(ShardId, secondAccountId), record.OwnerId);
    }

    [TestMethod]
    public async Task ContributeAsync_RevalidatesLiveAllegianceMembership_RatherThanTheStaleIdentityProjectionCache()
    {
        // VAULT-001 / CONTEXT.md: "a cache is permitted only when it is versioned/refreshed from ACE
        // and every sensitive action revalidates the current Acting Character." Seed a stale cache row
        // claiming this character belongs to no allegiance while live ace_shard says otherwise, and
        // prove the action still succeeds because it never consults the cache.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);

        // Stale cache: no MonarchId recorded at all for this character.
        var (staleRow, _) = CloudCharacterIdentityReadProjection.TryApply(
            null,
            CloudIdentityOutboxEvent.ForCharacterEvent(
                Guid.NewGuid(), ShardId, CloudIdentityEventType.CharacterRenamed, characterId, accountId, "Vassal", totalLogins: 0, sequenceNumber: 1));
        context.Add(staleRow);
        await context.SaveChangesAsync();

        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, personalOwnerId, Guid.NewGuid());

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.ContributeAsync(
            ShardId, accountId, characterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
    }

    [TestMethod]
    public async Task ContributeAsync_AMonarchWithLiveVassalsButNoOwnMonarchProperty_ActsForTheirOwnVault()
    {
        // A genuine monarch (one or more vassals currently pointing at them) has no Monarch property
        // of their own -- this must still resolve to their own vault, not "no allegiance."
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var monarchAccountId = NextId();
        var monarchCharacterId = NextId();
        var vassalCharacterId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, monarchCharacterId, monarchAccountId, "Monarch");
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, vassalCharacterId, NextId(), "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, vassalCharacterId, monarchCharacterId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, monarchAccountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, personalOwnerId, Guid.NewGuid());

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.ContributeAsync(
            ShardId, monarchAccountId, monarchCharacterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);

        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchCharacterId);
        var record = await context.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(vaultOwnerId, record.OwnerId);
    }

    [TestMethod]
    public async Task ContributeAsync_WhenTheVaultIsAtItsQuota_IsRejected()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();
        var alreadyInVaultBiotaId = NextId();
        var candidateBiotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, alreadyInVaultBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, candidateBiotaId);

        await using var context = new CloudDbContext(options);
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(alreadyInVaultBiotaId, ShardId, vaultOwnerId, Guid.NewGuid());
        await custodyBoundary.DepositAsync(candidateBiotaId, ShardId, personalOwnerId, Guid.NewGuid());

        var quotaBoundary = new CloudStorageQuotaLimitsBoundary(context);
        var current = await quotaBoundary.GetCurrentAsync(ShardId);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await quotaBoundary.SetVaultLimitAsync(ShardId, 1, actorAccessLevel: 5, current.Version.Value)).Kind);

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.ContributeAsync(
            ShardId, accountId, characterId, CloudReservationTarget.ForItem(new CloudItemId(candidateBiotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "Storage Quota");
    }

    [TestMethod]
    public async Task WhileFrozen_ContributeAsync_IsRefused()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var maintenanceContext = new CloudDbContext(options))
        {
            var maintenanceBoundary = new CloudGlobalMaintenanceBoundary(maintenanceContext);
            var initial = await maintenanceBoundary.GetCurrentAsync(ShardId);
            Assert.AreEqual(
                CloudBoundaryOutcomeKind.Committed,
                (await maintenanceBoundary.EnterAsync(ShardId, "downtime", confirmed: true, actorAccessLevel: 5, actorAccountId: 999, initial.Version.Value)).Kind);
        }

        await using var context = new CloudDbContext(options);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, personalOwnerId, Guid.NewGuid());

        var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
        var outcome = await gateway.ContributeAsync(
            ShardId, accountId, characterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "frozen");
    }

    [TestMethod]
    public async Task WDR007_AVaultOwnedItem_CanNeverBeReservedForWithdrawalByTheActingCharactersOwnAccount()
    {
        // WDR-007: "Allegiance Vault items cannot be withdrawn." Enforced by construction: the
        // withdrawal reservation path only ever authorizes an ordinary CloudOwnerIdentity.ForAccount
        // identity, which can never equal a vault's CloudOwnerIdentity.ForAllegianceVault identity.
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var accountId = NextId();
        var characterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, characterId, accountId, "Vassal");
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, characterId, monarchId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var context = new CloudDbContext(options);
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var custodyBoundary = new CloudCustodyBoundary(context);
        await custodyBoundary.DepositAsync(biotaId, ShardId, vaultOwnerId, Guid.NewGuid());

        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, accountId);
        var target = CloudWithdrawalReservationRequestTarget.ForItem(biotaId);
        var reservationOutcome = await custodyBoundary.ReserveForWithdrawalAsync(
            [target], ShardId, personalOwnerId, "tokenhash", TimeSpan.FromMinutes(15), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, reservationOutcome.Kind);
        StringAssert.Contains(reservationOutcome.Reason, "not owned by");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
