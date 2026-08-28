namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudWebSessionSecretHasherTests
{
    [TestMethod]
    public void Generate_ProducesAHighEntropySecretAndItsMatchingHash()
    {
        var secret = CloudWebSessionSecretHasher.Generate();

        Assert.IsFalse(string.IsNullOrWhiteSpace(secret.Secret));
        Assert.IsGreaterThanOrEqualTo(32, secret.Secret.Length);
        Assert.AreEqual(CloudWebSessionSecretHasher.Hash(secret.Secret), secret.Hash);
    }

    [TestMethod]
    public void Generate_NeverProducesTheSameSecretTwice()
    {
        var first = CloudWebSessionSecretHasher.Generate();
        var second = CloudWebSessionSecretHasher.Generate();

        Assert.AreNotEqual(first.Secret, second.Secret);
        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    [TestMethod]
    public void Hash_NeverReturnsTheSecretItself()
    {
        var secret = CloudWebSessionSecretHasher.Generate();

        Assert.AreNotEqual(secret.Secret, secret.Hash);
    }

    [TestMethod]
    public void Hash_RejectsAnEmptySecret()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudWebSessionSecretHasher.Hash(""));
    }
}
