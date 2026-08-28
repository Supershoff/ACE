using ACE.Common.Cryptography;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green coverage for issue #19's Red section: "Test bcrypt and legacy-hash migration
/// through the ACE verifier, wrong credentials..." (AUTH-002).
/// </summary>
[TestClass]
public sealed class CloudLegacyPasswordVerifierTests
{
    [TestMethod]
    public void Matches_BCryptAccount_CorrectPassword_ReturnsTrue()
    {
        var hash = BCryptProvider.HashPassword("correct horse battery staple", workFactor: 4);

        Assert.IsTrue(CloudLegacyPasswordVerifier.Matches(hash, "use bcrypt", "correct horse battery staple"));
    }

    [TestMethod]
    public void Matches_BCryptAccount_WrongPassword_ReturnsFalse()
    {
        var hash = BCryptProvider.HashPassword("correct horse battery staple", workFactor: 4);

        Assert.IsFalse(CloudLegacyPasswordVerifier.Matches(hash, "use bcrypt", "wrong password"));
    }

    [TestMethod]
    public void Matches_LegacySha512Account_CorrectPassword_ReturnsTrue()
    {
        var salt = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var hash = ComputeLegacyHashForTest("hunter2", salt);

        Assert.IsTrue(CloudLegacyPasswordVerifier.Matches(hash, salt, "hunter2"));
    }

    [TestMethod]
    public void Matches_LegacySha512Account_WrongPassword_ReturnsFalse()
    {
        var salt = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var hash = ComputeLegacyHashForTest("hunter2", salt);

        Assert.IsFalse(CloudLegacyPasswordVerifier.Matches(hash, salt, "not hunter2"));
    }

    [TestMethod]
    public void Matches_LegacySha512Account_CorruptedSalt_ReturnsFalseInsteadOfThrowing()
    {
        Assert.IsFalse(CloudLegacyPasswordVerifier.Matches("anything", "not-valid-base64!!!", "hunter2"));
    }

    [TestMethod]
    public void Matches_DifferentLengthHashes_ReturnsFalseInsteadOfThrowing()
    {
        var salt = Convert.ToBase64String(new byte[] { 1, 2, 3 });

        Assert.IsFalse(CloudLegacyPasswordVerifier.Matches("short", salt, "hunter2"));
    }

    private static string ComputeLegacyHashForTest(string password, string base64Salt)
    {
        var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
        var saltBytes = Convert.FromBase64String(base64Salt);
        var buffer = passwordBytes.Concat(saltBytes).ToArray();

        using var hasher = System.Security.Cryptography.SHA512.Create();
        return Convert.ToBase64String(hasher.ComputeHash(buffer));
    }
}
