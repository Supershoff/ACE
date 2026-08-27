namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudItemIdTests
{
    [TestMethod]
    public void Constructor_RejectsZero()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudItemId(0));
    }

    [TestMethod]
    public void Equality_IsValueBased()
    {
        var first = new CloudItemId(0x80000123);
        var second = new CloudItemId(0x80000123);
        var different = new CloudItemId(0x80000456);

        Assert.AreEqual(first, second);
        Assert.IsTrue(first == second);
        Assert.AreNotEqual(first, different);
        Assert.IsTrue(first != different);
    }

    [TestMethod]
    public void ToString_ReturnsUnderlyingValue()
    {
        var id = new CloudItemId(42);

        Assert.AreEqual("42", id.ToString());
    }
}
