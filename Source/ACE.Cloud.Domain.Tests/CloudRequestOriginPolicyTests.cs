namespace ACE.Cloud.Domain.Tests;

[TestClass]
public sealed class CloudRequestOriginPolicyTests
{
    private static readonly string[] AllowedOrigins = ["https://cloud.example.com"];

    [TestMethod]
    public void Evaluate_AllowedOrigin_IsAllowed()
    {
        var result = CloudRequestOriginPolicy.Evaluate("https://cloud.example.com", AllowedOrigins);

        Assert.IsTrue(result.IsAllowed);
    }

    [TestMethod]
    public void Evaluate_UnknownOrigin_IsDenied()
    {
        var result = CloudRequestOriginPolicy.Evaluate("https://attacker.example.com", AllowedOrigins);

        Assert.IsFalse(result.IsAllowed);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void Evaluate_MissingOrigin_IsDenied(string? origin)
    {
        var result = CloudRequestOriginPolicy.Evaluate(origin, AllowedOrigins);

        Assert.IsFalse(result.IsAllowed);
    }

    [TestMethod]
    public void Evaluate_OriginComparisonIsCaseInsensitive()
    {
        var result = CloudRequestOriginPolicy.Evaluate("HTTPS://CLOUD.EXAMPLE.COM", AllowedOrigins);

        Assert.IsTrue(result.IsAllowed);
    }
}
