namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudSupportedProtocolWindowTests
{
    [TestMethod]
    public void Constructor_MaximumBelowMinimum_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            new CloudSupportedProtocolWindow(new CloudProtocolVersion(2, 0, 0), new CloudProtocolVersion(1, 0, 0)));
    }

    [TestMethod]
    public void Constructor_MinimumEqualToMaximum_IsAllowed()
    {
        var window = new CloudSupportedProtocolWindow(new CloudProtocolVersion(1, 0, 0), new CloudProtocolVersion(1, 0, 0));

        Assert.IsTrue(window.Contains(new CloudProtocolVersion(1, 0, 0)));
    }

    [TestMethod]
    public void Contains_VersionInsideTheRange_IsTrue()
    {
        var window = new CloudSupportedProtocolWindow(new CloudProtocolVersion(1, 0, 0), new CloudProtocolVersion(2, 0, 0));

        Assert.IsTrue(window.Contains(new CloudProtocolVersion(1, 5, 0)));
    }

    [TestMethod]
    public void Contains_VersionOutsideTheRange_IsFalse()
    {
        var window = new CloudSupportedProtocolWindow(new CloudProtocolVersion(1, 0, 0), new CloudProtocolVersion(2, 0, 0));

        Assert.IsFalse(window.Contains(new CloudProtocolVersion(2, 0, 1)));
        Assert.IsFalse(window.Contains(new CloudProtocolVersion(0, 9, 9)));
    }
}
