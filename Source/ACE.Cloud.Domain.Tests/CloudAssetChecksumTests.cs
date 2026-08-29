namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudAssetChecksumTests
{
    private const string ValidSha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [TestMethod]
    public void TryParse_AValidLowercaseSha256_Succeeds()
    {
        var parsed = CloudAssetChecksum.TryParse(ValidSha256, out var checksum);

        Assert.IsTrue(parsed);
        Assert.AreEqual(ValidSha256, checksum.Value);
    }

    [TestMethod]
    public void TryParse_AnUppercaseSha256_NormalizesToLowercase()
    {
        var parsed = CloudAssetChecksum.TryParse(ValidSha256.ToUpperInvariant(), out var checksum);

        Assert.IsTrue(parsed);
        Assert.AreEqual(ValidSha256, checksum.Value);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("not-hex-at-all-and-way-too-short")]
    [DataRow("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")] // 63 chars
    [DataRow("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0")] // 65 chars
    [DataRow("g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")] // 64 chars, non-hex 'g'
    public void TryParse_MalformedInput_Fails(string? raw)
    {
        var parsed = CloudAssetChecksum.TryParse(raw, out _);

        Assert.IsFalse(parsed);
    }

    [TestMethod]
    public void Equals_IsCaseInsensitiveBecauseBothSidesNormalize()
    {
        CloudAssetChecksum.TryParse(ValidSha256, out var lower);
        CloudAssetChecksum.TryParse(ValidSha256.ToUpperInvariant(), out var upper);

        Assert.AreEqual(lower, upper);
    }
}
