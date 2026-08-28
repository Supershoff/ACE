namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudPrivateServiceKeyRingFactoryTests
{
    [TestMethod]
    public void Create_WithOnlyActiveKey_Succeeds()
    {
        var ring = CloudPrivateServiceKeyRingFactory.Create("k1", Convert.ToBase64String("secret-secret-secret-secret-32b"u8.ToArray()), null, null);

        Assert.AreEqual("k1", ring.ActiveKey.KeyId);
        Assert.IsNull(ring.PreviousKey);
    }

    [TestMethod]
    public void Create_WithBothKeys_Succeeds()
    {
        var ring = CloudPrivateServiceKeyRingFactory.Create(
            "k2", Convert.ToBase64String("new-secret-new-secret-new-sec32"u8.ToArray()),
            "k1", Convert.ToBase64String("old-secret-old-secret-old-sec32"u8.ToArray()));

        Assert.AreEqual("k2", ring.ActiveKey.KeyId);
        Assert.AreEqual("k1", ring.PreviousKey!.KeyId);
    }

    [TestMethod]
    public void Create_PreviousKeyIdWithoutSecret_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudPrivateServiceKeyRingFactory.Create(
            "k1", Convert.ToBase64String("secret-secret-secret-secret-32b"u8.ToArray()), "k0", null));
    }
}
