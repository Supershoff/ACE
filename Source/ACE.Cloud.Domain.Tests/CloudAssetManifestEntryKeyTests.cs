namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudAssetManifestEntryKeyTests
{
    [TestMethod]
    public void DidHex_IsAnEightDigitLowercaseHexString()
    {
        var key = new CloudAssetManifestEntryKey(0x06006C0Au, CloudAssetFileKind.Texture);

        Assert.AreEqual("06006c0a", key.DidHex);
    }

    [TestMethod]
    public void Constructor_ZeroDid_Throws()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudAssetManifestEntryKey(0, CloudAssetFileKind.Texture));
    }

    [TestMethod]
    public void ToString_CombinesKindAndHexDid()
    {
        var key = new CloudAssetManifestEntryKey(0x060011D3u, CloudAssetFileKind.Texture);

        Assert.AreEqual("texture/060011d3", key.ToString());
    }
}
