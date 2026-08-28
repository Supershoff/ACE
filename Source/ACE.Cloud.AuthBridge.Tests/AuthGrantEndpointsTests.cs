using System.Net;
using System.Net.Http.Json;
using System.Text;

using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;

namespace ACE.Cloud.AuthBridge.Tests;

/// <summary>
/// Red -> Green endpoint coverage for issue #19's Red section: "Test bcrypt and legacy-hash
/// migration through the ACE verifier, wrong credentials, linked-account credentials [not yet
/// distinguished before issue #20 -- AUTH-004's Main/Linked split is that issue's own aggregate],
/// banned/disabled accounts, rate limits, replayed/expired grants, and world process downtime" plus
/// "Test that passwords, account names, hashes, grants, cookies, and connection strings are redacted
/// from logs" and "private-service authentication."
/// </summary>
[TestClass]
public sealed class AuthGrantEndpointsTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static CloudPrivateServiceKeyRing SigningKeyRing() =>
        new(new CloudPrivateServiceKey(AuthBridgeTestFactory.ActiveServiceKeyId, Convert.FromBase64String(AuthBridgeTestFactory.ActiveServiceKeySecretBase64)));

    private static void SignRequest(HttpRequestMessage request, string method, string path, DateTime nowUtc)
    {
        request.Headers.Add(
            CloudPrivateServiceHeaders.SignatureHeaderName,
            CloudPrivateServiceRequestAuthenticator.Sign(method, path, nowUtc, SigningKeyRing()));
    }

    [TestMethod]
    public async Task IssueGrant_ValidBCryptCredentials_ReturnsASignedGrant()
    {
        await using var factory = new AuthBridgeTestFactory();
        var password = "correct horse battery staple";
        factory.AccountReader.Add(new CloudAceAccountSnapshot(
            101, "player1", ACE.Common.Cryptography.BCryptProvider.HashPassword(password, workFactor: 4), "use bcrypt", 0, null, null));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
        {
            Content = JsonContent.Create(new IssueGrantRequest("player1", password, CloudAuthAudiences.CloudBackend)),
        };
        SignRequest(request, "POST", "/internal/auth/grants", Now);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IssueGrantResponse>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(body!.Grant));
        Assert.AreEqual(101u, body.AccountId);
    }

    [TestMethod]
    public async Task IssueGrant_WrongPassword_ReturnsUnauthorized()
    {
        await using var factory = new AuthBridgeTestFactory();
        factory.AccountReader.Add(new CloudAceAccountSnapshot(
            101, "player1", ACE.Common.Cryptography.BCryptProvider.HashPassword("correct", workFactor: 4), "use bcrypt", 0, null, null));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
        {
            Content = JsonContent.Create(new IssueGrantRequest("player1", "wrong", CloudAuthAudiences.CloudBackend)),
        };
        SignRequest(request, "POST", "/internal/auth/grants", Now);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task IssueGrant_UnknownAccount_ReturnsUnauthorized()
    {
        await using var factory = new AuthBridgeTestFactory();

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
        {
            Content = JsonContent.Create(new IssueGrantRequest("nobody", "whatever", CloudAuthAudiences.CloudBackend)),
        };
        SignRequest(request, "POST", "/internal/auth/grants", Now);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task IssueGrant_BannedAccount_ReturnsForbidden()
    {
        await using var factory = new AuthBridgeTestFactory();
        var password = "correct";
        factory.AccountReader.Add(new CloudAceAccountSnapshot(
            101, "player1", ACE.Common.Cryptography.BCryptProvider.HashPassword(password, workFactor: 4), "use bcrypt", 0,
            BanExpireTime: Now.AddDays(1), BanReason: "cheating"));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
        {
            Content = JsonContent.Create(new IssueGrantRequest("player1", password, CloudAuthAudiences.CloudBackend)),
        };
        SignRequest(request, "POST", "/internal/auth/grants", Now);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task IssueGrant_LegacySha512Credentials_Verifies()
    {
        await using var factory = new AuthBridgeTestFactory();
        var salt = Convert.ToBase64String(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 });
        var password = "hunter2";
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var saltBytes = Convert.FromBase64String(salt);
        var hash = Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(passwordBytes.Concat(saltBytes).ToArray()));

        factory.AccountReader.Add(new CloudAceAccountSnapshot(102, "legacyplayer", hash, salt, 0, null, null));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
        {
            Content = JsonContent.Create(new IssueGrantRequest("legacyplayer", password, CloudAuthAudiences.CloudBackend)),
        };
        SignRequest(request, "POST", "/internal/auth/grants", Now);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task IssueGrant_MissingPrivateServiceSignature_ReturnsUnauthorized()
    {
        await using var factory = new AuthBridgeTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/internal/auth/grants", new IssueGrantRequest("player1", "whatever", CloudAuthAudiences.CloudBackend));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task IssueGrant_WrongPrivateServiceKey_ReturnsUnauthorized()
    {
        await using var factory = new AuthBridgeTestFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
        {
            Content = JsonContent.Create(new IssueGrantRequest("player1", "whatever", CloudAuthAudiences.CloudBackend)),
        };
        var wrongRing = new CloudPrivateServiceKeyRing(new CloudPrivateServiceKey("wrong-key", Encoding.UTF8.GetBytes("wrong-secret-wrong-secret-32byt")));
        request.Headers.Add(
            CloudPrivateServiceHeaders.SignatureHeaderName,
            CloudPrivateServiceRequestAuthenticator.Sign("POST", "/internal/auth/grants", Now, wrongRing));

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task IssueGrant_ExceedsRateLimit_ReturnsTooManyRequests()
    {
        await using var factory = new AuthBridgeTestFactory();
        factory.AccountReader.Add(new CloudAceAccountSnapshot(
            101, "ratelimited", ACE.Common.Cryptography.BCryptProvider.HashPassword("correct", workFactor: 4), "use bcrypt", 0, null, null));

        using var client = factory.CreateClient();

        // AuthBridgeTestFactory configures MaxLoginAttemptsPerWindow=3.
        for (var i = 0; i < 3; i++)
        {
            using var allowedRequest = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
            {
                Content = JsonContent.Create(new IssueGrantRequest("ratelimited", "wrong", CloudAuthAudiences.CloudBackend)),
            };
            SignRequest(allowedRequest, "POST", "/internal/auth/grants", Now);
            using var allowedResponse = await client.SendAsync(allowedRequest);
            Assert.AreNotEqual(HttpStatusCode.TooManyRequests, allowedResponse.StatusCode);
        }

        using var overLimitRequest = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
        {
            Content = JsonContent.Create(new IssueGrantRequest("ratelimited", "wrong", CloudAuthAudiences.CloudBackend)),
        };
        SignRequest(overLimitRequest, "POST", "/internal/auth/grants", Now);
        using var overLimitResponse = await client.SendAsync(overLimitRequest);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, overLimitResponse.StatusCode);
    }

    [TestMethod]
    public async Task IssueGrant_NeverLogsThePasswordOrAccountName()
    {
        await using var factory = new AuthBridgeTestFactory();
        var password = "super-secret-password-value";
        factory.AccountReader.Add(new CloudAceAccountSnapshot(
            103, "redaction-target-account", ACE.Common.Cryptography.BCryptProvider.HashPassword(password, workFactor: 4), "use bcrypt", 0, null, null));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
        {
            Content = JsonContent.Create(new IssueGrantRequest("redaction-target-account", password, CloudAuthAudiences.CloudBackend)),
        };
        SignRequest(request, "POST", "/internal/auth/grants", Now);

        using var response = await client.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        foreach (var message in factory.CapturedLogs.Messages)
        {
            Assert.IsFalse(message.Contains(password, StringComparison.Ordinal), $"Log message leaked the raw password: {message}");
            Assert.IsFalse(message.Contains("redaction-target-account", StringComparison.Ordinal), $"Log message leaked the account name: {message}");
        }
    }

    [TestMethod]
    public async Task GetAccessLevel_KnownAccount_ReturnsFreshAccessLevel()
    {
        await using var factory = new AuthBridgeTestFactory();
        factory.AccountReader.Add(new CloudAceAccountSnapshot(104, "admin1", "hash", "use bcrypt", 5, null, null));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/auth/access-level/104");
        SignRequest(request, "GET", "/internal/auth/access-level/104", Now);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AccessLevelResponse>();
        Assert.AreEqual(5u, body!.AccessLevel);
    }

    [TestMethod]
    public async Task GetAccessLevel_UnknownAccount_ReturnsNotFound()
    {
        await using var factory = new AuthBridgeTestFactory();

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/internal/auth/access-level/999");
        SignRequest(request, "GET", "/internal/auth/access-level/999", Now);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAccessLevel_MissingPrivateServiceSignature_ReturnsUnauthorized()
    {
        await using var factory = new AuthBridgeTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/internal/auth/access-level/104");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// AC Cloud Mule issue #19's acceptance criterion "New login continues during ACE world
    /// restarts while the bridge/database are healthy." Every test in this class already configures
    /// an unreachable <c>WorldBoundaryHealthEndpoint</c> (AuthBridgeTestFactory points it at
    /// 127.0.0.1:1); this test makes that decoupling explicit: the world-boundary readiness probe
    /// reports unhealthy while grant issuance -- which never consults it -- still succeeds.
    /// </summary>
    [TestMethod]
    public async Task IssueGrant_StillSucceeds_WhileTheWorldBoundaryReadinessProbeReportsUnhealthy()
    {
        await using var factory = new AuthBridgeTestFactory();
        var password = "correct horse battery staple";
        factory.AccountReader.Add(new CloudAceAccountSnapshot(
            105, "player-during-restart", ACE.Common.Cryptography.BCryptProvider.HashPassword(password, workFactor: 4), "use bcrypt", 0, null, null));

        using var client = factory.CreateClient();

        using var readinessResponse = await client.GetAsync("/health/ready");
        Assert.AreEqual(
            HttpStatusCode.ServiceUnavailable, readinessResponse.StatusCode,
            "Sanity check: the configured WorldBoundaryHealthEndpoint is unreachable in this test host.");

        using var grantRequest = new HttpRequestMessage(HttpMethod.Post, "/internal/auth/grants")
        {
            Content = JsonContent.Create(new IssueGrantRequest("player-during-restart", password, CloudAuthAudiences.CloudBackend)),
        };
        SignRequest(grantRequest, "POST", "/internal/auth/grants", Now);

        using var grantResponse = await client.SendAsync(grantRequest);

        Assert.AreEqual(HttpStatusCode.OK, grantResponse.StatusCode, "Grant issuance must not depend on the ACE world process being reachable.");
    }
}
