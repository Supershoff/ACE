namespace ACE.Cloud.Domain.Tests;

/// <summary>Coverage for WDR-001's "cryptographically strong one-way-verifiable tokens."</summary>
[TestClass]
public sealed class CloudWithdrawalTokenHasherTests
{
    [TestMethod]
    public void Generate_ProducesAHighEntropySecretAndItsMatchingHash()
    {
        var token = CloudWithdrawalTokenHasher.Generate();

        Assert.IsFalse(string.IsNullOrWhiteSpace(token.Secret));
        Assert.IsGreaterThanOrEqualTo(32, token.Secret.Length, "A 256-bit secret must render as a long string, not a short/guessable one.");
        Assert.AreEqual(CloudWithdrawalTokenHasher.Hash(token.Secret), token.Hash);
    }

    [TestMethod]
    public void Generate_NeverProducesTheSameSecretTwice()
    {
        var first = CloudWithdrawalTokenHasher.Generate();
        var second = CloudWithdrawalTokenHasher.Generate();

        Assert.AreNotEqual(first.Secret, second.Secret);
        Assert.AreNotEqual(first.Hash, second.Hash);
    }

    [TestMethod]
    public void Hash_IsDeterministicForTheSameSecret()
    {
        var token = CloudWithdrawalTokenHasher.Generate();

        var recomputed = CloudWithdrawalTokenHasher.Hash(token.Secret);

        Assert.AreEqual(token.Hash, recomputed);
    }

    [TestMethod]
    public void Hash_NeverReturnsTheSecretItself()
    {
        var token = CloudWithdrawalTokenHasher.Generate();

        Assert.AreNotEqual(token.Secret, token.Hash, "The hash must be a one-way verifier, never the secret itself.");
    }

    [TestMethod]
    public void Hash_RejectsAnEmptySecret()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudWithdrawalTokenHasher.Hash(""));
    }

    [TestMethod]
    public void Hash_ADifferentSecret_ProducesADifferentHash()
    {
        var a = CloudWithdrawalTokenHasher.Generate();
        var b = CloudWithdrawalTokenHasher.Generate();

        Assert.AreNotEqual(CloudWithdrawalTokenHasher.Hash(a.Secret), CloudWithdrawalTokenHasher.Hash(b.Secret));
    }
}
