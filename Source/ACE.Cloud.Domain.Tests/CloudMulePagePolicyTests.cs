namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #30's Red requirement: "Test page boundaries at 0, 1, 101, 102, 103, and large inventories;
/// ... automatic page creation/removal" (UI-002).
/// </summary>
[TestClass]
public sealed class CloudMulePagePolicyTests
{
    [TestMethod]
    public void GetPageCount_Zero_ReturnsZeroPages()
    {
        Assert.AreEqual(0, CloudMulePagePolicy.GetPageCount(0));
    }

    [TestMethod]
    public void GetPageCount_One_ReturnsOnePage()
    {
        Assert.AreEqual(1, CloudMulePagePolicy.GetPageCount(1));
    }

    [TestMethod]
    public void GetPageCount_ExactlyOnePageSize_ReturnsOnePage()
    {
        Assert.AreEqual(1, CloudMulePagePolicy.GetPageCount(101));
        Assert.AreEqual(1, CloudMulePagePolicy.GetPageCount(102));
    }

    [TestMethod]
    public void GetPageCount_OneOverAPageSize_ReturnsTwoPages()
    {
        Assert.AreEqual(2, CloudMulePagePolicy.GetPageCount(103));
    }

    [TestMethod]
    public void GetPageCount_LargeInventory_RoundsUp()
    {
        Assert.AreEqual(10, CloudMulePagePolicy.GetPageCount(102 * 10));
        Assert.AreEqual(11, CloudMulePagePolicy.GetPageCount((102 * 10) + 1));
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(101, 1)]
    [DataRow(102, 2)]
    [DataRow(203, 2)]
    [DataRow(204, 3)]
    public void GetPageNumber_ReturnsThePageOwningThatZeroBasedRank(int zeroBasedRank, int expectedPageNumber)
    {
        Assert.AreEqual(expectedPageNumber, CloudMulePagePolicy.GetPageNumber(zeroBasedRank));
    }

    [TestMethod]
    public void PageExists_Zero_NoPagesExist()
    {
        Assert.IsFalse(CloudMulePagePolicy.PageExists(1, totalItemCount: 0));
    }

    [TestMethod]
    public void PageExists_ExactlyOnePage_SecondPageDoesNotExistYet()
    {
        Assert.IsTrue(CloudMulePagePolicy.PageExists(1, totalItemCount: 102));
        Assert.IsFalse(CloudMulePagePolicy.PageExists(2, totalItemCount: 102));
    }

    [TestMethod]
    public void PageExists_OneOverAPageSize_SecondPageAutomaticallyExists()
    {
        Assert.IsTrue(CloudMulePagePolicy.PageExists(2, totalItemCount: 103));
    }

    [TestMethod]
    public void PageExists_AutomaticRemoval_TrailingPageStopsExistingOnceItemCountDrops()
    {
        // Simulates withdrawing the 103rd item: page 2 existed, then automatically stops existing --
        // there is no separate delete step (UI-002: "created or removed automatically").
        Assert.IsTrue(CloudMulePagePolicy.PageExists(2, totalItemCount: 103));
        Assert.IsFalse(CloudMulePagePolicy.PageExists(2, totalItemCount: 102));
    }

    [TestMethod]
    public void FormatPageName_MatchesTheDocumentedBracketFormat()
    {
        Assert.AreEqual("[Melee Weapons] Mule 1", CloudMulePagePolicy.FormatPageName(CloudInventoryCategory.MeleeWeapons, 1));
        Assert.AreEqual("[Miscellaneous] Mule 3", CloudMulePagePolicy.FormatPageName(CloudInventoryCategory.Miscellaneous, 3));
    }

    [TestMethod]
    public void GetPageNumber_NegativeRank_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CloudMulePagePolicy.GetPageNumber(-1));
    }

    [TestMethod]
    public void PageExists_NonPositivePageNumber_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => CloudMulePagePolicy.PageExists(0, totalItemCount: 10));
    }
}
