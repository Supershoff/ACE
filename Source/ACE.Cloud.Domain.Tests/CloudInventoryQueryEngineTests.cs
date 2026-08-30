namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #30's Red requirement: "Test owner, Sharing Grant, admin, revoked access, cross-shard IDs,
/// and projection lag/version responses" (the authorization half; cross-shard/version/lag are
/// exercised against a real database in ACE.Cloud.PersistenceIntegrationTests) composed with page
/// boundaries and filters/sorts at the pure Domain layer.
/// </summary>
[TestClass]
public sealed class CloudInventoryQueryEngineTests
{
    [TestMethod]
    public void Authorize_Owner_SeesTheirOwnItems()
    {
        var owner = Guid.NewGuid();
        var candidates = new[] { Candidate(1, owner) };

        var authorized = CloudInventoryQueryEngine.Authorize(candidates, CloudLiveStreamViewer.ForOwners([owner])).ToList();

        Assert.HasCount(1, authorized);
    }

    [TestMethod]
    public void Authorize_UnrelatedViewer_SeesNothing()
    {
        var candidates = new[] { Candidate(1, Guid.NewGuid()) };

        var authorized = CloudInventoryQueryEngine.Authorize(candidates, CloudLiveStreamViewer.ForOwners([Guid.NewGuid()])).ToList();

        Assert.IsEmpty(authorized);
    }

    [TestMethod]
    public void Authorize_SharingGrant_ViewerAuthorizedForAnotherOwner_SeesThatOwnersItems()
    {
        // A Sharing Grant is modeled the same way the Live State Stream already models it: the
        // caller composes the grantee's own ownership group plus every owner who currently shares
        // with them into one authorized-owner set (see CloudLiveStreamViewer's doc comment).
        var granteeOwner = Guid.NewGuid();
        var grantorOwner = Guid.NewGuid();
        var candidates = new[] { Candidate(1, grantorOwner) };

        var viewer = CloudLiveStreamViewer.ForOwners([granteeOwner, grantorOwner]);
        var authorized = CloudInventoryQueryEngine.Authorize(candidates, viewer).ToList();

        Assert.HasCount(1, authorized);
    }

    [TestMethod]
    public void Authorize_RevokedSharingGrant_NoLongerSeesThatOwnersItemsOnTheNextQuery()
    {
        var grantorOwner = Guid.NewGuid();
        var candidates = new[] { Candidate(1, grantorOwner) };

        var beforeRevocation = CloudInventoryQueryEngine.Authorize(candidates, CloudLiveStreamViewer.ForOwners([grantorOwner])).ToList();
        Assert.HasCount(1, beforeRevocation);

        var afterRevocation = CloudInventoryQueryEngine.Authorize(candidates, CloudLiveStreamViewer.ForOwners([])).ToList();
        Assert.IsEmpty(afterRevocation);
    }

    [TestMethod]
    public void Authorize_Admin_SeesEveryOwnersItems()
    {
        var candidates = new[] { Candidate(1, Guid.NewGuid()), Candidate(2, Guid.NewGuid()) };

        var authorized = CloudInventoryQueryEngine.Authorize(candidates, CloudLiveStreamViewer.ForAdmin()).ToList();

        Assert.HasCount(2, authorized);
    }

    [TestMethod]
    public void Query_CategoryFilter_OnlyReturnsThatCategory()
    {
        var owner = Guid.NewGuid();
        var candidates = new[]
        {
            Candidate(1, owner, category: CloudInventoryCategory.Armor),
            Candidate(2, owner, category: CloudInventoryCategory.Gems),
        };

        var result = CloudInventoryQueryEngine.Query(
            candidates, CloudInventoryCategory.Gems, pageNumber: 1, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);

        Assert.HasCount(1, result.Items);
        Assert.AreEqual(CloudInventoryCategory.Gems, result.Items[0].Category);
    }

    [TestMethod]
    public void Query_NoCategoryFilter_ReturnsEveryCategory_ForSpreadsheetView()
    {
        var owner = Guid.NewGuid();
        var candidates = new[]
        {
            Candidate(1, owner, category: CloudInventoryCategory.Armor),
            Candidate(2, owner, category: CloudInventoryCategory.Gems),
        };

        var result = CloudInventoryQueryEngine.Query(
            candidates, category: null, pageNumber: 1, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);

        Assert.HasCount(2, result.Items);
        Assert.IsNull(result.PageName);
    }

    [TestMethod]
    public void Query_PageBoundary_102Items_OnePageExists_SecondPageDoesNot()
    {
        var owner = Guid.NewGuid();
        var candidates = Enumerable.Range(1, 102).Select(i => Candidate((uint)i, owner)).ToArray();

        var page1 = CloudInventoryQueryEngine.Query(
            candidates, CloudInventoryCategory.Miscellaneous, 1, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);
        Assert.IsTrue(page1.PageExists);
        Assert.HasCount(102, page1.Items);
        Assert.AreEqual(1, page1.TotalPages);

        var page2 = CloudInventoryQueryEngine.Query(
            candidates, CloudInventoryCategory.Miscellaneous, 2, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);
        Assert.IsFalse(page2.PageExists);
        Assert.IsEmpty(page2.Items);
    }

    [TestMethod]
    public void Query_PageBoundary_103Items_SecondPageHasExactlyOneItem()
    {
        var owner = Guid.NewGuid();
        var candidates = Enumerable.Range(1, 103).Select(i => Candidate((uint)i, owner)).ToArray();

        var page2 = CloudInventoryQueryEngine.Query(
            candidates, CloudInventoryCategory.Miscellaneous, 2, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);

        Assert.IsTrue(page2.PageExists);
        Assert.HasCount(1, page2.Items);
        Assert.AreEqual(2, page2.TotalPages);
    }

    [TestMethod]
    public void Query_ZeroItems_NoPageExists()
    {
        var result = CloudInventoryQueryEngine.Query(
            [], CloudInventoryCategory.Miscellaneous, 1, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);

        Assert.IsFalse(result.PageExists);
        Assert.AreEqual(0, result.TotalPages);
    }

    [TestMethod]
    public void Query_StackLots_TwoLotsOfTheSameBiota_AreDistinctRows()
    {
        var owner = Guid.NewGuid();
        var itemId = new CloudItemId(1);
        var candidates = new[]
        {
            new CloudInventoryQueryCandidate(
                itemId, new CloudStackLotId(Guid.NewGuid()), owner, "Trade Note", CloudInventoryCategory.Currency,
                Quantity: 5, Value: 1, Burden: 1, IsReserved: false, CloudAggregateVersion.Initial),
            new CloudInventoryQueryCandidate(
                itemId, new CloudStackLotId(Guid.NewGuid()), owner, "Trade Note", CloudInventoryCategory.Currency,
                Quantity: 3, Value: 1, Burden: 1, IsReserved: false, CloudAggregateVersion.Initial),
        };

        var result = CloudInventoryQueryEngine.Query(
            candidates, CloudInventoryCategory.Currency, 1, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);

        Assert.HasCount(2, result.Items);
        Assert.AreNotEqual(result.Items[0].StackLotId, result.Items[1].StackLotId);
    }

    [TestMethod]
    public void Query_ReservedItem_PermittedActionsExcludeMutation()
    {
        var owner = Guid.NewGuid();
        var candidates = new[] { Candidate(1, owner, isReserved: true) };

        var result = CloudInventoryQueryEngine.Query(
            candidates, CloudInventoryCategory.Miscellaneous, 1, CloudInventorySortKey.Name, CloudInventorySortDirection.Ascending);

        var permittedActions = result.Items[0].PermittedActions;
        Assert.IsFalse(permittedActions.CanWithdraw);
        Assert.IsFalse(permittedActions.CanList);
        Assert.IsFalse(permittedActions.CanTransfer);
    }

    private static CloudInventoryQueryCandidate Candidate(
        uint itemId,
        Guid owner,
        CloudInventoryCategory category = CloudInventoryCategory.Miscellaneous,
        bool isReserved = false) =>
        new(
            new CloudItemId(itemId),
            StackLotId: null,
            OwnerId: owner,
            Name: $"Item {itemId}",
            Category: category,
            Quantity: 1,
            Value: null,
            Burden: null,
            IsReserved: isReserved,
            Version: CloudAggregateVersion.Initial);
}
