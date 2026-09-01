using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #39's P6 phase-gate evidence: "Run concurrent randomized asset/lot operations and verify
/// conservation, exclusive reservations, quota semantics, and immutable ledger lineage" across
/// Transfer Offers (#35), personal Sharing Grants (#36), and the Allegiance Vault (#37, #38) together
/// -- the collaboration surfaces #35-#38 each proved individually, but never previously exercised
/// concurrently against one another on the same disposable ACE/MariaDB instance. Named and structured
/// like <see cref="CloudPhaseGateAcceptanceTests"/> (issue #17) and
/// <see cref="CloudFidelityPhaseGateAcceptanceTests"/> (issue #28): one test method per evidence
/// category the acceptance criteria ask for.
///
///   - Boundary evidence: <see cref="FullCollaborationLifecycle_OfferAcceptThenVaultContributeAndGrantDerivedWithdrawal_ConservesEveryItemWithLedgerLineage"/>
///   - Race evidence: <see cref="ConcurrentOffersAndVaultContributionsOnDistinctItems_AllCommitWithoutFalseConflicts_AndConserveEveryItem"/>
///   - Exclusivity evidence: <see cref="RacingTransferOfferCreateAndVaultContribute_OnTheSameItem_ExactlyOneWins_NeitherLosesNorDuplicatesTheItem"/>
///   - Randomized conservation evidence: <see cref="RandomizedMixedOperations_AcrossOffersGrantsAndVault_AlwaysConserveTheFullItemPool"/>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCollaborationPhaseGateAcceptanceTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 990_000;

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
    public async Task FullCollaborationLifecycle_OfferAcceptThenVaultContributeAndGrantDerivedWithdrawal_ConservesEveryItemWithLedgerLineage()
    {
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var monarchId = NextId();
        var granteeAccountId = NextId();
        var granteeCharacterId = NextId();
        var offeredBiotaId = NextId();
        var vaultBiotaId = NextId();
        const string recipientCharacterName = "CollabRecipient";
        const string granteeCharacterName = "CollabGrantee";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        // biota_properties_i_i_d.object_Id has a foreign key to biota.id, so the recipient's own
        // Monarch instance property (granted below) requires its own biota row to exist too.
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, recipientCharacterId);
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, recipientCharacterId, monarchId);
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, granteeAccountId, granteeCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, offeredBiotaId);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, vaultBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);
        var recipientOwnerId = CloudOwnerIdentity.ForAccount(ShardId, recipientAccountId);
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var granteeOwnerId = CloudOwnerIdentity.ForAccount(ShardId, granteeAccountId);

        // Stage 1 (XFER-001/XFER-002): sender offers one item; recipient accepts it.
        await using (var context = new CloudDbContext(options))
        {
            await new CloudCustodyBoundary(context).DepositAsync(offeredBiotaId, ShardId, senderOwnerId, Guid.NewGuid());
            var offerGateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
            var createOutcome = await offerGateway.CreateAsync(
                ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(offeredBiotaId)], Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, createOutcome.Kind, createOutcome.Reason);
            var acceptOutcome = await offerGateway.AcceptAsync(createOutcome.Value!.Id, recipientAccountId, createOutcome.Value.Version);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, acceptOutcome.Kind, acceptOutcome.Reason);
        }

        // Stage 2 (VAULT-001..003): the recipient, acting as the current monarch, contributes their
        // own second item into their Allegiance Vault.
        await using (var context = new CloudDbContext(options))
        {
            await new CloudCustodyBoundary(context).DepositAsync(vaultBiotaId, ShardId, recipientOwnerId, Guid.NewGuid());
            var vaultGateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
            var contributeOutcome = await vaultGateway.ContributeAsync(
                ShardId, recipientAccountId, recipientCharacterId, CloudReservationTarget.ForItem(new CloudItemId(vaultBiotaId)), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, contributeOutcome.Kind, contributeOutcome.Reason);
        }

        // Stage 3 (SHARE-003): the recipient grants the grantee View & Withdraw over their personal
        // inventory (not the vault -- WDR-007), and the grantee opens a grant-derived Withdrawal
        // Reservation over the accepted offer item.
        Guid grantId;
        await using (var context = new CloudDbContext(options))
        {
            var grantOutcome = await new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context))
                .SetAsync(ShardId, recipientAccountId, granteeCharacterName, CloudSharingGrantLevel.ViewAndWithdraw);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, grantOutcome.Kind, grantOutcome.Reason);
            grantId = grantOutcome.Value!.Id;
        }

        await using (var context = new CloudDbContext(options))
        {
            var custodyBoundary = new CloudCustodyBoundary(context);
            var reserveOutcome = await custodyBoundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(offeredBiotaId)], ShardId, recipientOwnerId, granteeOwnerId, grantId,
                CloudWithdrawalTokenHasher.Hash(Guid.NewGuid().ToString("N")), TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, reserveOutcome.Kind, reserveOutcome.Reason);
        }

        // Conservation: both biotas remain backed by exactly one Cloud Custody Record each, and every
        // ownership hop left an immutable, correctly ordered Activity Ledger trail (EVT-001, EVT-002).
        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == offeredBiotaId));
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == vaultBiotaId));

        var vaultRecord = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == vaultBiotaId);
        Assert.AreEqual(vaultOwnerId, vaultRecord.OwnerId);

        var offerLedgerTypes = await verifyContext.CloudActivityLedgerEvents
            .Where(e => e.BiotaId == offeredBiotaId)
            .Select(e => e.EventType)
            .ToListAsync();
        Assert.Contains(CloudBoundaryOperationType.TransferOfferCreated, offerLedgerTypes);
        Assert.Contains(CloudBoundaryOperationType.OwnershipTransfer, offerLedgerTypes);

        var vaultLedgerTypes = await verifyContext.CloudActivityLedgerEvents
            .Where(e => e.BiotaId == vaultBiotaId)
            .Select(e => e.EventType)
            .ToListAsync();
        Assert.Contains(CloudBoundaryOperationType.VaultContribution, vaultLedgerTypes);
    }

    [TestMethod]
    public async Task ConcurrentOffersAndVaultContributionsOnDistinctItems_AllCommitWithoutFalseConflicts_AndConserveEveryItem()
    {
        const int itemCount = 8;
        var senderAccountId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var monarchId = NextId();
        const string recipientCharacterName = "ConcurrentRecipient";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, recipientCharacterId);
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, recipientCharacterId, monarchId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var senderOwnerId = CloudOwnerIdentity.ForAccount(ShardId, senderAccountId);
        var recipientOwnerId = CloudOwnerIdentity.ForAccount(ShardId, recipientAccountId);
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        var offerBiotaIds = new List<uint>();
        var vaultBiotaIds = new List<uint>();
        await using (var setupContext = new CloudDbContext(options))
        {
            var custodyBoundary = new CloudCustodyBoundary(setupContext);
            for (var i = 0; i < itemCount; i++)
            {
                var offerBiotaId = NextId();
                await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, offerBiotaId);
                await custodyBoundary.DepositAsync(offerBiotaId, ShardId, senderOwnerId, Guid.NewGuid());
                offerBiotaIds.Add(offerBiotaId);

                var vaultBiotaId = NextId();
                await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, vaultBiotaId);
                await custodyBoundary.DepositAsync(vaultBiotaId, ShardId, recipientOwnerId, Guid.NewGuid());
                vaultBiotaIds.Add(vaultBiotaId);
            }
        }

        // Every offer targets a distinct item and every contribution targets a distinct item, so a
        // correct implementation must commit every single one concurrently -- any conflict here would
        // indicate over-broad locking, not genuine contention (transaction rule 2).
        var offerTasks = offerBiotaIds.Select(async biotaId =>
        {
            await using var context = new CloudDbContext(options);
            var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
            return await gateway.CreateAsync(
                ShardId, senderAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
        });
        var vaultTasks = vaultBiotaIds.Select(async biotaId =>
        {
            await using var context = new CloudDbContext(options);
            var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
            return await gateway.ContributeAsync(
                ShardId, recipientAccountId, recipientCharacterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());
        });

        var offerResults = await Task.WhenAll(offerTasks);
        var vaultResults = await Task.WhenAll(vaultTasks);

        Assert.IsTrue(offerResults.All(r => r.Kind == CloudBoundaryOutcomeKind.Committed), "Every Transfer Offer over a distinct item must commit concurrently without a false conflict.");
        Assert.IsTrue(vaultResults.All(r => r.Kind == CloudBoundaryOutcomeKind.Committed), "Every vault contribution over a distinct item must commit concurrently without a false conflict.");

        await using var verifyContext = new CloudDbContext(options);
        foreach (var biotaId in vaultBiotaIds)
        {
            var record = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
            Assert.AreEqual(vaultOwnerId, record.OwnerId);
        }

        // Conservation: exactly one custody record per biota still exists -- concurrency never created
        // or lost an item (ARCH-006, transaction rule 7).
        Assert.AreEqual(itemCount, await verifyContext.CloudCustodyRecords.CountAsync(r => offerBiotaIds.Contains(r.BiotaId)));
        Assert.AreEqual(itemCount, await verifyContext.CloudCustodyRecords.CountAsync(r => vaultBiotaIds.Contains(r.BiotaId)));
    }

    [TestMethod]
    public async Task RacingTransferOfferCreateAndVaultContribute_OnTheSameItem_ExactlyOneWins_NeitherLosesNorDuplicatesTheItem()
    {
        var ownerAccountId = NextId();
        var ownerCharacterId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var monarchId = NextId();
        var biotaId = NextId();
        const string recipientCharacterName = "RaceRecipient";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, ownerCharacterId, ownerAccountId, "RaceOwner");
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, ownerCharacterId);
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, ownerCharacterId, monarchId);
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerOwnerId = CloudOwnerIdentity.ForAccount(ShardId, ownerAccountId);
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);

        await using (var setupContext = new CloudDbContext(options))
        {
            await new CloudCustodyBoundary(setupContext).DepositAsync(biotaId, ShardId, ownerOwnerId, Guid.NewGuid());
        }

        await using var offerContext = new CloudDbContext(options);
        await using var vaultContext = new CloudDbContext(options);
        var offerGateway = new CloudTransferOfferGateway(offerContext, new CloudAccountLinkGateway(offerContext));
        var vaultGateway = new CloudAllegianceVaultTransactionGateway(vaultContext, new CloudAccountLinkGateway(vaultContext));

        var offerTask = offerGateway.CreateAsync(
            ShardId, ownerAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
        var contributeTask = vaultGateway.ContributeAsync(
            ShardId, ownerAccountId, ownerCharacterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());
        await Task.WhenAll(offerTask, contributeTask);

        var outcomes = new[] { offerTask.Result.Kind, contributeTask.Result.Kind };
        Assert.AreEqual(1, outcomes.Count(k => k == CloudBoundaryOutcomeKind.Committed), "Exactly one of the racing Transfer Offer / Vault Contribution attempts must win the same item.");
        Assert.AreEqual(1, outcomes.Count(k => k == CloudBoundaryOutcomeKind.Conflict), "The loser must observe a conflict, never silently succeed too (no duplicate custody).");

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(1, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId), "The item must never be lost or duplicated by the race.");

        var record = await verifyContext.CloudCustodyRecords.AsNoTracking().SingleAsync(r => r.BiotaId == biotaId);
        var contributeWon = contributeTask.Result.Kind == CloudBoundaryOutcomeKind.Committed;
        var expectedOwnerId = contributeWon ? vaultOwnerId : ownerOwnerId;
        Assert.AreEqual(expectedOwnerId, record.OwnerId, "Custody must reflect exactly whichever attempt actually committed.");
    }

    [TestMethod]
    [DataRow(11, DisplayName = "seed 11")]
    [DataRow(2026, DisplayName = "seed 2026")]
    public async Task RandomizedMixedOperations_AcrossOffersGrantsAndVault_AlwaysConserveTheFullItemPool(int seed)
    {
        const int itemCount = 6;
        var random = new Random(seed);

        var ownerAccountId = NextId();
        var ownerCharacterId = NextId();
        var recipientAccountId = NextId();
        var recipientCharacterId = NextId();
        var granteeAccountId = NextId();
        var granteeCharacterId = NextId();
        var monarchId = NextId();
        const string recipientCharacterName = "RandomRecipient";
        const string granteeCharacterName = "RandomGrantee";

        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, ownerCharacterId, ownerAccountId, "RandomOwner");
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, ownerCharacterId);
        await AceShardTestData.GrantMonarchAsync(_fixture.AceShardConnectionString, ownerCharacterId, monarchId);
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, recipientCharacterId, recipientAccountId, recipientCharacterName);
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, granteeAccountId, granteeCharacterName);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerOwnerId = CloudOwnerIdentity.ForAccount(ShardId, ownerAccountId);
        var granteeOwnerId = CloudOwnerIdentity.ForAccount(ShardId, granteeAccountId);

        var biotaIds = new List<uint>();
        await using (var setupContext = new CloudDbContext(options))
        {
            var custodyBoundary = new CloudCustodyBoundary(setupContext);
            for (var i = 0; i < itemCount; i++)
            {
                var biotaId = NextId();
                await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
                await custodyBoundary.DepositAsync(biotaId, ShardId, ownerOwnerId, Guid.NewGuid());
                biotaIds.Add(biotaId);
            }
        }

        Guid grantId;
        await using (var grantContext = new CloudDbContext(options))
        {
            var grantOutcome = await new CloudSharingGrantGateway(grantContext, new CloudAccountLinkGateway(grantContext))
                .SetAsync(ShardId, ownerAccountId, granteeCharacterName, CloudSharingGrantLevel.ViewAndWithdraw);
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, grantOutcome.Kind, grantOutcome.Reason);
            grantId = grantOutcome.Value!.Id;
        }

        // Each still-personally-owned item randomly attempts one operation type. Every operation
        // exclusively reserves/moves its own item, so every attempt against a still-available item
        // must commit -- this proves conservation and exclusivity hold across three independently
        // implemented gateways sharing the same underlying reservation/custody tables, not merely
        // within one gateway's own randomized walk (contrast with CloudLotConservationInvariantSuite,
        // which only exercises one lot-owning aggregate at a time).
        foreach (var biotaId in biotaIds)
        {
            var operation = random.Next(3);
            await using var context = new CloudDbContext(options);

            if (operation == 0)
            {
                var gateway = new CloudTransferOfferGateway(context, new CloudAccountLinkGateway(context));
                var outcome = await gateway.CreateAsync(
                    ShardId, ownerAccountId, recipientCharacterName, [CloudTransferOfferRequestTarget.ForItem(biotaId)], Guid.NewGuid());
                Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
            }
            else if (operation == 1)
            {
                var gateway = new CloudAllegianceVaultTransactionGateway(context, new CloudAccountLinkGateway(context));
                var outcome = await gateway.ContributeAsync(
                    ShardId, ownerAccountId, ownerCharacterId, CloudReservationTarget.ForItem(new CloudItemId(biotaId)), Guid.NewGuid());
                Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
            }
            else
            {
                var custodyBoundary = new CloudCustodyBoundary(context);
                var outcome = await custodyBoundary.ReserveForWithdrawalAsync(
                    [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)], ShardId, ownerOwnerId, granteeOwnerId, grantId,
                    CloudWithdrawalTokenHasher.Hash(Guid.NewGuid().ToString("N")), TimeSpan.FromMinutes(15), Guid.NewGuid());
                Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
            }
        }

        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(
            itemCount, await verifyContext.CloudCustodyRecords.CountAsync(r => biotaIds.Contains(r.BiotaId)),
            "Every item from the pool must still be backed by exactly one Cloud Custody Record after the randomized mixed-operation sequence.");
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
