using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Entity.Enum;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #30's Red requirement against a real database: "Test page boundaries at 0, 1, 101, 102,
/// 103, and large inventories; lots/quantities; automatic page creation/removal ... Test owner,
/// Sharing Grant, admin, revoked access, cross-shard IDs, and projection lag/version responses."
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudInventoryQueryReaderTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 900_000;

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
    public async Task QueryAsync_Owner_SeesTheirOwnItem()
    {
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor);

        var response = await QueryAsync(CloudLiveStreamViewer.ForOwners([owner]));

        Assert.HasCount(1, response.Page.Items);
        Assert.AreEqual("Ivory Buckler", response.Page.Items[0].Name);
    }

    [TestMethod]
    public async Task QueryAsync_UnrelatedViewer_SeesNothing()
    {
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor);

        var response = await QueryAsync(CloudLiveStreamViewer.ForOwners([Guid.NewGuid()]));

        Assert.IsEmpty(response.Page.Items);
    }

    [TestMethod]
    public async Task QueryAsync_SharingGrant_ViewerAuthorizedForAnotherOwner_SeesThatOwnersItem()
    {
        var granteeOwner = Guid.NewGuid();
        var grantorOwner = Guid.NewGuid();
        await SeedWholeItemAsync(grantorOwner, "Shared Cloak", ItemType.Clothing);

        // Simulates a resolved Sharing Grant the way CloudLiveStreamViewer's own doc comment
        // describes: the caller composes the grantee's own group plus every currently-sharing owner
        // into one authorized-owner set before calling the reader.
        var response = await QueryAsync(CloudLiveStreamViewer.ForOwners([granteeOwner, grantorOwner]), CloudInventoryCategory.Clothing);

        Assert.HasCount(1, response.Page.Items);
    }

    [TestMethod]
    public async Task QueryAsync_RevokedSharingGrant_NoLongerReturnedOnTheNextQuery()
    {
        var grantorOwner = Guid.NewGuid();
        await SeedWholeItemAsync(grantorOwner, "Shared Cloak", ItemType.Clothing);

        var beforeRevocation = await QueryAsync(CloudLiveStreamViewer.ForOwners([grantorOwner]), CloudInventoryCategory.Clothing);
        Assert.HasCount(1, beforeRevocation.Page.Items);

        var afterRevocation = await QueryAsync(CloudLiveStreamViewer.ForOwners([]), CloudInventoryCategory.Clothing);
        Assert.IsEmpty(afterRevocation.Page.Items);
    }

    [TestMethod]
    public async Task QueryAsync_Admin_SeesEveryOwnersItems()
    {
        await SeedWholeItemAsync(Guid.NewGuid(), "Item One", ItemType.Armor);
        await SeedWholeItemAsync(Guid.NewGuid(), "Item Two", ItemType.Armor);

        var response = await QueryAsync(CloudLiveStreamViewer.ForAdmin());

        Assert.HasCount(2, response.Page.Items);
    }

    [TestMethod]
    public async Task QueryAsync_MismatchedShardId_ReturnsNothingEvenForAnAdmin()
    {
        // ARCH-001 makes literal cross-shard row coexistence impossible within one deployment's
        // schema (CloudShardBinding is a database-enforced singleton -- see
        // CK_CloudShardBinding_Singleton and its unique ShardId index), so every row here can only
        // ever carry this deployment's one bound ShardId. This test instead proves the reader itself
        // never trusts an unscoped/global fetch: passing a shard identifier that does not match the
        // deployment's bound shard returns nothing, exactly as it must if this schema ever legitimately
        // held more than one shard's rows.
        var owner = Guid.NewGuid();
        await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor);

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventoryQueryReader(context);

        var response = await reader.QueryAsync(
            "a-different-shard", CloudLiveStreamViewer.ForAdmin(), new CloudInventoryQueryRequest { Category = CloudInventoryCategory.Armor });

        Assert.IsEmpty(response.Page.Items);
    }

    [TestMethod]
    public async Task QueryAsync_102Items_OnePageExists_SecondPageDoesNot()
    {
        var owner = Guid.NewGuid();
        for (var i = 0; i < 102; i++)
        {
            await SeedWholeItemAsync(owner, $"Item {i:D3}", ItemType.Armor);
        }

        var page1 = await QueryAsync(CloudLiveStreamViewer.ForOwners([owner]), page: 1);
        Assert.IsTrue(page1.Page.PageExists);
        Assert.HasCount(102, page1.Page.Items);

        var page2 = await QueryAsync(CloudLiveStreamViewer.ForOwners([owner]), page: 2);
        Assert.IsFalse(page2.Page.PageExists);
        Assert.IsEmpty(page2.Page.Items);
    }

    [TestMethod]
    public async Task QueryAsync_103Items_SecondPageAutomaticallyExistsWithOneItem()
    {
        var owner = Guid.NewGuid();
        for (var i = 0; i < 103; i++)
        {
            await SeedWholeItemAsync(owner, $"Item {i:D3}", ItemType.Armor);
        }

        var page2 = await QueryAsync(CloudLiveStreamViewer.ForOwners([owner]), page: 2);

        Assert.IsTrue(page2.Page.PageExists);
        Assert.HasCount(1, page2.Page.Items);
        Assert.AreEqual(2, page2.Page.TotalPages);
    }

    [TestMethod]
    public async Task QueryAsync_ZeroItems_NoPageExists()
    {
        var response = await QueryAsync(CloudLiveStreamViewer.ForOwners([Guid.NewGuid()]));

        Assert.IsFalse(response.Page.PageExists);
        Assert.AreEqual(0, response.Page.TotalPages);
    }

    [TestMethod]
    public async Task QueryAsync_StackLots_EachLotIsItsOwnRowWithItsOwnQuantity()
    {
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var biotaId = NextBiotaId();

        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await SeedItemPropertiesAsync(biotaId, "Trade Note", ItemType.Money);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using (var setupContext = new CloudDbContext(options))
        {
            var record = CloudCustodyRecord.CreateStack(biotaId, ShardId, totalQuantity: 8, Guid.NewGuid());
            setupContext.CloudCustodyRecords.Add(record);
            setupContext.CloudStackLots.Add(new CloudStackLot(record.Id, ShardId, firstOwner, quantity: 5));
            setupContext.CloudStackLots.Add(new CloudStackLot(record.Id, ShardId, secondOwner, quantity: 3));
            await setupContext.SaveChangesAsync();
        }

        var firstOwnerResponse = await QueryAsync(CloudLiveStreamViewer.ForOwners([firstOwner]), CloudInventoryCategory.Currency);
        Assert.HasCount(1, firstOwnerResponse.Page.Items);
        Assert.AreEqual(5, firstOwnerResponse.Page.Items[0].Quantity);

        var adminResponse = await QueryAsync(CloudLiveStreamViewer.ForAdmin(), CloudInventoryCategory.Currency);
        Assert.HasCount(2, adminResponse.Page.Items);
        Assert.AreNotEqual(adminResponse.Page.Items[0].StackLotId, adminResponse.Page.Items[1].StackLotId);
    }

    [TestMethod]
    public async Task QueryAsync_ActivelyReservedItem_ReportsReservedAndNoMutationPermittedActions()
    {
        var owner = Guid.NewGuid();
        var biotaId = await SeedWholeItemAsync(owner, "Reserved Sword", ItemType.MeleeWeapon);

        await using (var reserveContext = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var boundary = new CloudCustodyBoundary(reserveContext);
            var outcome = await boundary.ReserveForWithdrawalAsync(
                [CloudWithdrawalReservationRequestTarget.ForItem(biotaId)],
                ShardId, owner, Convert.ToHexString(Guid.NewGuid().ToByteArray()), TimeSpan.FromMinutes(15), Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        }

        var response = await QueryAsync(CloudLiveStreamViewer.ForOwners([owner]), CloudInventoryCategory.MeleeWeapons);

        Assert.HasCount(1, response.Page.Items);
        Assert.IsTrue(response.Page.Items[0].IsReserved);
        Assert.IsFalse(response.Page.Items[0].PermittedActions.CanWithdraw);
    }

    [TestMethod]
    public async Task QueryAsync_ReportsCustodyProjectionCheckpointAsProjectionLagSignal()
    {
        await using (var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            context.CloudProjectionCheckpoints.Add(new CloudProjectionCheckpoint(CloudCustodyProjectionConsumer.ConsumerName, ShardId));
            await context.SaveChangesAsync();

            var checkpoint = await context.CloudProjectionCheckpoints
                .SingleAsync(c => c.ConsumerName == CloudCustodyProjectionConsumer.ConsumerName);
            checkpoint.Advance(7);
            await context.SaveChangesAsync();
        }

        var response = await QueryAsync(CloudLiveStreamViewer.ForAdmin());

        Assert.AreEqual(7, response.AsOfCustodyOutboxSequenceNumber);
    }

    [TestMethod]
    public async Task QueryAsync_NoCheckpointYet_ReportsZeroLag()
    {
        var response = await QueryAsync(CloudLiveStreamViewer.ForAdmin());

        Assert.AreEqual(0, response.AsOfCustodyOutboxSequenceNumber);
    }

    [TestMethod]
    public async Task IsItemVisibleToViewerAsync_Owner_ReturnsTrue()
    {
        var owner = Guid.NewGuid();
        var biotaId = await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor);

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventoryQueryReader(context);

        Assert.IsTrue(await reader.IsItemVisibleToViewerAsync(ShardId, CloudLiveStreamViewer.ForOwners([owner]), new CloudItemId(biotaId)));
    }

    [TestMethod]
    public async Task IsItemVisibleToViewerAsync_UnrelatedViewer_ReturnsFalse()
    {
        var owner = Guid.NewGuid();
        var biotaId = await SeedWholeItemAsync(owner, "Ivory Buckler", ItemType.Armor);

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventoryQueryReader(context);

        Assert.IsFalse(await reader.IsItemVisibleToViewerAsync(ShardId, CloudLiveStreamViewer.ForOwners([Guid.NewGuid()]), new CloudItemId(biotaId)));
    }

    [TestMethod]
    public async Task IsItemVisibleToViewerAsync_Admin_ReturnsTrueForAnyExistingItem()
    {
        var biotaId = await SeedWholeItemAsync(Guid.NewGuid(), "Ivory Buckler", ItemType.Armor);

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventoryQueryReader(context);

        Assert.IsTrue(await reader.IsItemVisibleToViewerAsync(ShardId, CloudLiveStreamViewer.ForAdmin(), new CloudItemId(biotaId)));
    }

    [TestMethod]
    public async Task IsItemVisibleToViewerAsync_StackLotOwner_ReturnsTrueForThatLotsOwnerOnly()
    {
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await SeedItemPropertiesAsync(biotaId, "Trade Note", ItemType.Money);

        await using (var setupContext = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)))
        {
            var record = CloudCustodyRecord.CreateStack(biotaId, ShardId, totalQuantity: 8, Guid.NewGuid());
            setupContext.CloudCustodyRecords.Add(record);
            setupContext.CloudStackLots.Add(new CloudStackLot(record.Id, ShardId, firstOwner, quantity: 5));
            setupContext.CloudStackLots.Add(new CloudStackLot(record.Id, ShardId, secondOwner, quantity: 3));
            await setupContext.SaveChangesAsync();
        }

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventoryQueryReader(context);

        Assert.IsTrue(await reader.IsItemVisibleToViewerAsync(ShardId, CloudLiveStreamViewer.ForOwners([firstOwner]), new CloudItemId(biotaId)));
        Assert.IsFalse(await reader.IsItemVisibleToViewerAsync(ShardId, CloudLiveStreamViewer.ForOwners([Guid.NewGuid()]), new CloudItemId(biotaId)));
    }

    [TestMethod]
    public async Task IsItemVisibleToViewerAsync_MismatchedShardId_ReturnsFalseEvenForAnAdmin()
    {
        var biotaId = await SeedWholeItemAsync(Guid.NewGuid(), "Ivory Buckler", ItemType.Armor);

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventoryQueryReader(context);

        Assert.IsFalse(await reader.IsItemVisibleToViewerAsync("a-different-shard", CloudLiveStreamViewer.ForAdmin(), new CloudItemId(biotaId)));
    }

    private async Task<CloudInventoryQueryResponse> QueryAsync(
        CloudLiveStreamViewer viewer, CloudInventoryCategory? category = CloudInventoryCategory.Armor, int page = 1)
    {
        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var reader = new CloudInventoryQueryReader(context);
        return await reader.QueryAsync(ShardId, viewer, new CloudInventoryQueryRequest { Category = category, Page = page });
    }

    private async Task<uint> SeedWholeItemAsync(Guid owner, string name, ItemType itemType)
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await SeedItemPropertiesAsync(biotaId, name, itemType);

        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        context.CloudCustodyRecords.Add(new CloudCustodyRecord(biotaId, ShardId, owner, Guid.NewGuid()));
        await context.SaveChangesAsync();

        return biotaId;
    }

    private async Task SeedItemPropertiesAsync(uint biotaId, string name, ItemType itemType)
    {
        await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var gateway = new CloudInventoryItemPropertiesGateway(context);
        await gateway.UpsertAsync(biotaId, ShardId, name, itemType, WeenieType.Generic, value: null, burden: null, iconCacheKeyHex: null, revision: 1);
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
