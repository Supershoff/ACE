namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudShardIdTests
{
    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    public void Constructor_RequiresNonEmptyValue(string? value)
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CloudShardId(value!));
    }

    [TestMethod]
    public void Constructor_TrimsSurroundingWhitespace()
    {
        var shardId = new CloudShardId(" us1 ");

        Assert.AreEqual("us1", shardId.Value);
    }

    [TestMethod]
    public void Equality_IsValueBased()
    {
        var first = new CloudShardId("us1");
        var second = new CloudShardId("us1");
        var different = new CloudShardId("us2");

        Assert.AreEqual(first, second);
        Assert.IsTrue(first == second);
        Assert.AreNotEqual(first, different);
        Assert.IsTrue(first != different);
    }
}
