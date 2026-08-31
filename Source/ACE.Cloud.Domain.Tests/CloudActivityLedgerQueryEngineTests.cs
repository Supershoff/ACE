namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #34's Red: "Test owner/shared/admin/vault ledger scopes ... pagination, correlation IDs."
/// "Owner", "Shared", and "Vault" are exercised here as three different
/// <see cref="CloudLiveStreamViewer.AuthorizedOwnerIds"/> compositions over the exact same
/// <see cref="CloudActivityLedgerQueryEngine.Authorize"/> call -- see that type's doc comment for why
/// there is no separate per-scope code path to test independently.
/// </summary>
[TestClass]
public sealed class CloudActivityLedgerQueryEngineTests
{
    private static CloudActivityLedgerEntry Entry(
        Guid? ownerId = null,
        CloudActivityLedgerCategory category = CloudActivityLedgerCategory.CustodyBoundary,
        DateTime? occurredAtUtc = null,
        uint biotaId = 123) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "us1",
            category,
            "Deposit",
            ownerId,
            biotaId,
            "Committed",
            null,
            occurredAtUtc ?? DateTime.UtcNow);

    [TestMethod]
    public void Authorize_OwnerScope_SeesOnlyTheirOwnCustodyBoundaryEvents()
    {
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var viewer = CloudLiveStreamViewer.ForOwners([owner]);

        var mine = Entry(owner);
        var theirs = Entry(stranger);

        var authorized = CloudActivityLedgerQueryEngine.Authorize([mine, theirs], viewer).ToList();

        CollectionAssert.AreEqual(new[] { mine }, authorized);
    }

    [TestMethod]
    public void Authorize_SharedScope_SeesEventsForEveryAuthorizedOwnerIdIncludingAGrantor()
    {
        // "Shared" is modeled as additional authorized owner IDs the caller adds to the same set
        // Owner scope uses (the seam SHARE-001..004 will populate once Sharing Grants exist) --
        // proven here with a second owner ID standing in for a grantor's.
        var owner = Guid.NewGuid();
        var grantor = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var viewer = CloudLiveStreamViewer.ForOwners([owner, grantor]);

        var mine = Entry(owner);
        var sharedToMe = Entry(grantor);
        var unrelated = Entry(stranger);

        var authorized = CloudActivityLedgerQueryEngine.Authorize([mine, sharedToMe, unrelated], viewer).ToList();

        Assert.HasCount(2, authorized);
        Assert.Contains(mine, authorized);
        Assert.Contains(sharedToMe, authorized);
    }

    [TestMethod]
    public void Authorize_VaultScope_SeesEventsForAnAddedAllegianceVaultOwnerId()
    {
        // Vault scope is the same mechanism: the caller adds the viewer's current Allegiance Vault
        // owner ID(s) to AuthorizedOwnerIds (CloudOwnerIdentity.ForAllegianceVault) before calling
        // Authorize -- modeled here directly as an additional owner ID.
        var owner = Guid.NewGuid();
        var vaultOwnerId = Guid.NewGuid();
        var otherVaultOwnerId = Guid.NewGuid();
        var viewer = CloudLiveStreamViewer.ForOwners([owner, vaultOwnerId]);

        var vaultEvent = Entry(vaultOwnerId);
        var otherVaultEvent = Entry(otherVaultOwnerId);

        var authorized = CloudActivityLedgerQueryEngine.Authorize([vaultEvent, otherVaultEvent], viewer).ToList();

        CollectionAssert.AreEqual(new[] { vaultEvent }, authorized);
    }

    [TestMethod]
    public void Authorize_AdminScope_SeesEveryCategoryIncludingAdminOnlyOwnerlessEvents()
    {
        var viewer = CloudLiveStreamViewer.ForAdmin();

        var custody = Entry(Guid.NewGuid());
        var accountLink = Entry(ownerId: null, category: CloudActivityLedgerCategory.AccountLink);
        var globalMaintenance = Entry(ownerId: null, category: CloudActivityLedgerCategory.GlobalMaintenance);
        var assetImport = Entry(ownerId: null, category: CloudActivityLedgerCategory.AssetImport);

        var authorized = CloudActivityLedgerQueryEngine.Authorize(
            [custody, accountLink, globalMaintenance, assetImport], viewer).ToList();

        Assert.HasCount(4, authorized);
    }

    [TestMethod]
    public void Authorize_NonAdminViewer_NeverSeesAnAdminOnlyOwnerlessEvent()
    {
        var viewer = CloudLiveStreamViewer.ForOwners([Guid.NewGuid()]);
        var accountLink = Entry(ownerId: null, category: CloudActivityLedgerCategory.AccountLink);

        var authorized = CloudActivityLedgerQueryEngine.Authorize([accountLink], viewer).ToList();

        Assert.HasCount(0, authorized);
    }

    [TestMethod]
    public void Paginate_OrdersNewestFirstAndSlicesByPage()
    {
        var owner = Guid.NewGuid();
        var older = Entry(owner, occurredAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = Entry(owner, occurredAtUtc: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var newest = Entry(owner, occurredAtUtc: new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));

        var firstPage = CloudActivityLedgerQueryEngine.Paginate([older, newer, newest], pageNumber: 1, pageSize: 2);
        Assert.AreEqual(3, firstPage.TotalCount);
        Assert.AreEqual(2, firstPage.TotalPages);
        CollectionAssert.AreEqual(new[] { newest, newer }, firstPage.Entries.ToList());

        var secondPage = CloudActivityLedgerQueryEngine.Paginate([older, newer, newest], pageNumber: 2, pageSize: 2);
        CollectionAssert.AreEqual(new[] { older }, secondPage.Entries.ToList());
    }

    [TestMethod]
    public void Paginate_EveryEntryRetainsItsOwnCorrelationId()
    {
        var owner = Guid.NewGuid();
        var entry = Entry(owner);

        var page = CloudActivityLedgerQueryEngine.Paginate([entry], pageNumber: 1, pageSize: 10);

        Assert.AreEqual(entry.CorrelationId, page.Entries.Single().CorrelationId);
    }

    [TestMethod]
    public void Paginate_RejectsANonPositivePageNumber()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            CloudActivityLedgerQueryEngine.Paginate([], pageNumber: 0, pageSize: 10));
    }
}
