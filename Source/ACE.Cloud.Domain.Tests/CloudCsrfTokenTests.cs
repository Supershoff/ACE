namespace ACE.Cloud.Domain.Tests;

/// <summary>Red -> Green coverage for issue #19's Red section: "Test CSRF, origin..." (security baseline).</summary>
[TestClass]
public sealed class CloudCsrfTokenTests
{
    [TestMethod]
    public void Generate_NeverProducesTheSameTokenTwice()
    {
        var first = CloudCsrfTokenGenerator.Generate();
        var second = CloudCsrfTokenGenerator.Generate();

        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void Matches_SameToken_ReturnsTrue()
    {
        var token = CloudCsrfTokenGenerator.Generate();

        Assert.IsTrue(CloudCsrfTokenValidator.Matches(token, token));
    }

    [TestMethod]
    public void Matches_DifferentTokens_ReturnsFalse()
    {
        Assert.IsFalse(CloudCsrfTokenValidator.Matches(CloudCsrfTokenGenerator.Generate(), CloudCsrfTokenGenerator.Generate()));
    }

    [TestMethod]
    [DataRow(null, "session-token")]
    [DataRow("submitted-token", null)]
    [DataRow(null, null)]
    [DataRow("", "")]
    public void Matches_MissingEitherSide_ReturnsFalse(string? submitted, string? session)
    {
        Assert.IsFalse(CloudCsrfTokenValidator.Matches(submitted, session));
    }
}
