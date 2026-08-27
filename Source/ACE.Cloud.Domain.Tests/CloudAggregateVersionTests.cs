namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// ARCH-006, transaction rule 3: every mutable aggregate carries a positive, comparable version
/// that a stale expected value can be checked against (see also CloudCommandGuardTests).
/// </summary>
[TestClass]
public sealed class CloudAggregateVersionTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_RejectsNonPositiveValues(int value)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudAggregateVersion(value));
    }

    [TestMethod]
    public void Initial_IsOne()
    {
        Assert.AreEqual(1, CloudAggregateVersion.Initial.Value);
    }

    [TestMethod]
    public void Next_IncrementsByOne()
    {
        var version = new CloudAggregateVersion(5);

        Assert.AreEqual(6, version.Next().Value);
    }

    [TestMethod]
    public void Equality_And_Ordering_AreValueBased()
    {
        var first = new CloudAggregateVersion(3);
        var second = new CloudAggregateVersion(3);
        var later = new CloudAggregateVersion(4);

        Assert.AreEqual(first, second);
        Assert.IsTrue(first == second);
        Assert.IsTrue(first < later);
        Assert.IsTrue(later > first);
        Assert.AreNotEqual(first, later);
    }
}
