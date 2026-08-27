namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudProtocolVersionTests
{
    [TestMethod]
    public void Parse_ValidMajorMinorPatch_RoundTripsThroughToString()
    {
        var version = CloudProtocolVersion.Parse("2.10.3");

        Assert.AreEqual(2, version.Major);
        Assert.AreEqual(10, version.Minor);
        Assert.AreEqual(3, version.Patch);
        Assert.AreEqual("2.10.3", version.ToString());
    }

    [TestMethod]
    [DataRow("2.0", DisplayName = "missing patch")]
    [DataRow("2.0.0.0", DisplayName = "too many components")]
    [DataRow("2.a.0", DisplayName = "non-numeric component")]
    [DataRow("", DisplayName = "empty")]
    [DataRow(null, DisplayName = "null")]
    public void TryParse_InvalidFormats_ReturnFalse(string? version)
    {
        Assert.IsFalse(CloudProtocolVersion.TryParse(version, out _));
    }

    [TestMethod]
    public void CompareTo_OrdersByMajorThenMinorThenPatch()
    {
        Assert.IsTrue(new CloudProtocolVersion(1, 9, 9) < new CloudProtocolVersion(2, 0, 0));
        Assert.IsTrue(new CloudProtocolVersion(2, 0, 0) < new CloudProtocolVersion(2, 1, 0));
        Assert.IsTrue(new CloudProtocolVersion(2, 1, 0) < new CloudProtocolVersion(2, 1, 1));
        Assert.IsTrue(new CloudProtocolVersion(2, 1, 1) > new CloudProtocolVersion(2, 1, 0));
    }

    [TestMethod]
    public void Equals_SameComponents_AreEqual()
    {
        Assert.AreEqual(new CloudProtocolVersion(1, 2, 3), new CloudProtocolVersion(1, 2, 3));
        Assert.IsTrue(new CloudProtocolVersion(1, 2, 3) == new CloudProtocolVersion(1, 2, 3));
    }
}
