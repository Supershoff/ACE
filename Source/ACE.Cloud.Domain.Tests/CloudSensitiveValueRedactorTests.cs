namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green coverage for issue #19's Red section: "Test that passwords, account names, hashes,
/// grants, cookies, and connection strings are redacted from logs, traces, errors, and webhook
/// payloads."
/// </summary>
[TestClass]
public sealed class CloudSensitiveValueRedactorTests
{
    [TestMethod]
    public void Redact_RemovesEachSensitiveValue()
    {
        var message = "Login failed for account player1 with password hunter2 (connection Server=x;Pwd=y;)";

        var result = CloudSensitiveValueRedactor.Redact(message, "player1", "hunter2", "Server=x;Pwd=y;");

        Assert.IsFalse(result.Contains("player1", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("hunter2", StringComparison.Ordinal));
        Assert.IsFalse(result.Contains("Server=x;Pwd=y;", StringComparison.Ordinal));
        StringAssert.Contains(result, "[redacted]");
    }

    [TestMethod]
    public void Redact_LeavesNonSensitiveTextIntact()
    {
        var message = "Login failed: invalid credentials.";

        var result = CloudSensitiveValueRedactor.Redact(message, "some-password");

        Assert.AreEqual(message, result);
    }

    [TestMethod]
    public void Redact_IgnoresNullOrEmptySensitiveValues()
    {
        var message = "hello world";

        var result = CloudSensitiveValueRedactor.Redact(message, null, "", "   ".Trim());

        Assert.AreEqual(message, result);
    }

    [TestMethod]
    public void Redact_RedactsGrantTokenAndCookieValues()
    {
        var grant = "eyJhY2NvdW50IjoxfQ.k1.c2lnbmF0dXJl";
        var cookie = "session-secret-value";
        var message = $"exchanged grant {grant} for cookie {cookie}";

        var result = CloudSensitiveValueRedactor.Redact(message, grant, cookie);

        Assert.IsFalse(result.Contains(grant, StringComparison.Ordinal));
        Assert.IsFalse(result.Contains(cookie, StringComparison.Ordinal));
    }
}
