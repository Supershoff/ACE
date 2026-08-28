namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Coverage for <see cref="CloudCustodianPosition.TryParse"/> against ACE's own LOC format
/// (DEP-007's "custom full ACE position strings" and "invalid positions" Red tests).
/// </summary>
[TestClass]
public sealed class CloudCustodianPositionTests
{
    private const string ExampleFromTheIssue =
        "0x00030146 [122.346077 -88.811691 -11.995001] 0.181943 0.000000 0.000000 -0.983309";

    [TestMethod]
    public void TryParse_TheExampleStringFromTheIssue_ParsesEveryComponent()
    {
        var position = CloudCustodianPosition.TryParse(ExampleFromTheIssue);

        Assert.IsNotNull(position);
        Assert.AreEqual(0x00030146u, position.Landblock);
        Assert.AreEqual(122.346077f, position.X);
        Assert.AreEqual(-88.811691f, position.Y);
        Assert.AreEqual(-11.995001f, position.Z);
        Assert.AreEqual(0.181943f, position.RotationW);
        Assert.AreEqual(0.000000f, position.RotationX);
        Assert.AreEqual(0.000000f, position.RotationY);
        Assert.AreEqual(-0.983309f, position.RotationZ);
        Assert.AreEqual(ExampleFromTheIssue, position.Raw);
    }

    [TestMethod]
    public void TryParse_ALandblockWithoutTheLeading0x_StillParses()
    {
        var position = CloudCustodianPosition.TryParse("00030146 122.346077 -88.811691 -11.995001 0.181943 0.000000 0.000000 -0.983309");

        Assert.IsNotNull(position);
        Assert.AreEqual(0x00030146u, position.Landblock);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("not a position at all")]
    [DataRow("0xZZZZZZZZ [1 2 3] 1 0 0 0")]
    [DataRow("0x00030146 [122.346077 -88.811691 -11.995001] 0.181943 0.000000 0.000000")]
    [DataRow("0x00030146 [122.346077 -88.811691 -11.995001] 0.181943 0.000000 0.000000 -0.983309 1.0")]
    public void TryParse_AnInvalidString_ReturnsNull(string? raw)
    {
        Assert.IsNull(CloudCustodianPosition.TryParse(raw));
    }

    [TestMethod]
    public void Equals_TwoStringsForTheSameLandblockAndCoordinates_AreConsideredTheSameLocation()
    {
        var a = CloudCustodianPosition.TryParse("0x00030146 [1.000000 2.000000 3.000000] 1.000000 0.000000 0.000000 0.000000")!;
        var b = CloudCustodianPosition.TryParse("0x00030146 [1.000000 2.000000 3.000000] 0.707107 0.000000 0.000000 0.707107")!;

        Assert.IsTrue(a.Equals(b), "Two positions at the same landblock and coordinates must be duplicates regardless of facing.");
    }

    [TestMethod]
    public void Equals_ADifferentLandblock_IsNotADuplicate()
    {
        var a = CloudCustodianPosition.TryParse("0x00030146 [1.000000 2.000000 3.000000] 1.000000 0.000000 0.000000 0.000000")!;
        var b = CloudCustodianPosition.TryParse("0x00030147 [1.000000 2.000000 3.000000] 1.000000 0.000000 0.000000 0.000000")!;

        Assert.IsFalse(a.Equals(b));
    }
}
