using System.Text;

namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudPrivateServiceKeyRingTests
{
    [TestMethod]
    public void TryGetKey_FindsActiveKey()
    {
        var active = new CloudPrivateServiceKey("k1", Encoding.UTF8.GetBytes("secret"));
        var ring = new CloudPrivateServiceKeyRing(active);

        Assert.IsTrue(ring.TryGetKey("k1", out var found));
        Assert.AreSame(active, found);
    }

    [TestMethod]
    public void TryGetKey_FindsPreviousKeyDuringOverlap()
    {
        var active = new CloudPrivateServiceKey("k2", Encoding.UTF8.GetBytes("new-secret"));
        var previous = new CloudPrivateServiceKey("k1", Encoding.UTF8.GetBytes("old-secret"));
        var ring = new CloudPrivateServiceKeyRing(active, previous);

        Assert.IsTrue(ring.TryGetKey("k1", out var found));
        Assert.AreSame(previous, found);
    }

    [TestMethod]
    public void TryGetKey_UnknownKeyId_ReturnsFalse()
    {
        var ring = new CloudPrivateServiceKeyRing(new CloudPrivateServiceKey("k1", Encoding.UTF8.GetBytes("secret")));

        Assert.IsFalse(ring.TryGetKey("unknown", out _));
    }

    [TestMethod]
    public void TryGetKey_NullKeyId_ReturnsFalse()
    {
        var ring = new CloudPrivateServiceKeyRing(new CloudPrivateServiceKey("k1", Encoding.UTF8.GetBytes("secret")));

        Assert.IsFalse(ring.TryGetKey(null, out _));
    }

    [TestMethod]
    public void Constructor_RejectsEmptySecret()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CloudPrivateServiceKey("k1", []));
    }

    [TestMethod]
    public void Constructor_RejectsDuplicateKeyIdsBetweenActiveAndPrevious()
    {
        var active = new CloudPrivateServiceKey("k1", Encoding.UTF8.GetBytes("secret-a"));
        var previous = new CloudPrivateServiceKey("k1", Encoding.UTF8.GetBytes("secret-b"));

        Assert.ThrowsExactly<ArgumentException>(() => new CloudPrivateServiceKeyRing(active, previous));
    }
}
