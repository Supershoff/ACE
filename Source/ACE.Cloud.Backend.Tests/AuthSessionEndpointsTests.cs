using System.Net;
using System.Net.Http.Json;

using ACE.Cloud.Domain;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// Red -> Green endpoint coverage for issue #19's Green bullet "Exchange grants in the backend for
/// secure HttpOnly SameSite sessions with CSRF and strict origin controls" and ADM-001
/// ("Revalidate ACE access level 5 from authority data on every sensitive admin request").
/// </summary>
[TestClass]
public sealed class AuthSessionEndpointsTests
{
    private static readonly CloudPrivateServiceKeyRing SharedKeyRing = new(
        new CloudPrivateServiceKey("test-active-key", Convert.FromBase64String("dGVzdC1hY3RpdmUtc2VjcmV0LTMyLWJ5dGVzLWxvbmc=")));

    private static string ValidGrantFor(uint accountId, DateTime nowUtc) =>
        CloudAuthGrantIssuer.Issue(accountId, CloudAuthAudiences.CloudBackend, nowUtc, TimeSpan.FromSeconds(30), SharedKeyRing);

    private static void UseAllowedOrigin(HttpRequestMessage request) =>
        request.Headers.Add("Origin", BackendTestFactory.AllowedOrigin);

    [TestMethod]
    public async Task Login_ValidCredentials_SetsAnHttpOnlySecureSameSiteStrictCookie_AndReturnsACsrfToken()
    {
        await using var factory = new BackendTestFactory();
        var now = DateTime.UtcNow;
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Issued, ValidGrantFor(42, now));

        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("player1", "hunter2")),
        };
        UseAllowedOrigin(request);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(body!.CsrfToken));

        Assert.IsTrue(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var cookie = cookies!.Single(c => c.StartsWith("ace_cloud_session=", StringComparison.Ordinal)).ToLowerInvariant();
        StringAssert.Contains(cookie, "httponly");
        StringAssert.Contains(cookie, "secure");
        StringAssert.Contains(cookie, "samesite=strict");
    }

    [TestMethod]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.InvalidCredentials, Grant: null);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("player1", "wrong")),
        };
        UseAllowedOrigin(request);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_BannedAccount_StillReturnsGenericUnauthorized_NotAccountEnumerable()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.AccountBanned, Grant: null);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("player1", "whatever")),
        };
        UseAllowedOrigin(request);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_AuthBridgeUnavailable_ReturnsServiceUnavailable()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Unavailable, Grant: null);

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("player1", "whatever")),
        };
        UseAllowedOrigin(request);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_MissingOrigin_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Issued, ValidGrantFor(42, DateTime.UtcNow));

        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest("player1", "hunter2"));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_UnknownOrigin_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Issued, ValidGrantFor(42, DateTime.UtcNow));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("player1", "hunter2")),
        };
        request.Headers.Add("Origin", "https://attacker.example.com");

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Login_ExceedsRateLimit_ReturnsTooManyRequests()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.InvalidCredentials, Grant: null);

        using var client = factory.CreateClient();

        // BackendTestFactory configures MaxLoginAttemptsPerWindow=3.
        for (var i = 0; i < 3; i++)
        {
            using var allowedRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest("player1", "wrong")),
            };
            UseAllowedOrigin(allowedRequest);
            using var allowedResponse = await client.SendAsync(allowedRequest);
            Assert.AreNotEqual(HttpStatusCode.TooManyRequests, allowedResponse.StatusCode);
        }

        using var overLimitRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("player1", "wrong")),
        };
        UseAllowedOrigin(overLimitRequest);
        using var overLimitResponse = await client.SendAsync(overLimitRequest);

        Assert.AreEqual(HttpStatusCode.TooManyRequests, overLimitResponse.StatusCode);
    }

    [TestMethod]
    public async Task Login_ReplayedGrant_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        var now = DateTime.UtcNow;
        var grant = ValidGrantFor(42, now);
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Issued, grant);

        using var client = factory.CreateClient();

        using (var firstRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/login") { Content = JsonContent.Create(new LoginRequest("player1", "hunter2")) })
        {
            UseAllowedOrigin(firstRequest);
            using var firstResponse = await client.SendAsync(firstRequest);
            Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        }

        // Same underlying grant returned again (simulating a captured/replayed Auth Bridge response).
        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, "/auth/login") { Content = JsonContent.Create(new LoginRequest("player1", "hunter2")) };
        UseAllowedOrigin(secondRequest);
        using var secondResponse = await client.SendAsync(secondRequest);

        Assert.AreEqual(HttpStatusCode.Unauthorized, secondResponse.StatusCode);
    }

    [TestMethod]
    public async Task Logout_WithoutCsrfToken_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        var now = DateTime.UtcNow;
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            "us1", 42, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromHours(1));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        UseAllowedOrigin(request);
        request.Headers.Add("Cookie", $"ace_cloud_session={secret.Secret}");

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Logout_WithCorrectCsrfToken_RevokesTheSession()
    {
        await using var factory = new BackendTestFactory();
        var now = DateTime.UtcNow;
        var secret = CloudWebSessionSecretHasher.Generate();
        var csrfToken = CloudCsrfTokenGenerator.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync("us1", 42, Guid.NewGuid(), secret.Hash, csrfToken, now, TimeSpan.FromHours(1));

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        UseAllowedOrigin(request);
        request.Headers.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsNull(await factory.SessionStore.TryGetActiveSessionAsync(secret.Hash, now.AddSeconds(1)));
    }

    [TestMethod]
    public async Task AdminWhoAmI_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/admin/whoami");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminWhoAmI_SessionExistsButFreshAccessLevelIsNotFive_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        var now = DateTime.UtcNow;
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            "us1", 42, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromHours(1));
        factory.AuthBridgeClient.NextAccessLevel = 1;

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/whoami");
        request.Headers.Add("Cookie", $"ace_cloud_session={secret.Secret}");

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminWhoAmI_FreshAccessLevelFive_ReturnsOk_AndAlwaysCallsTheFreshEndpoint()
    {
        await using var factory = new BackendTestFactory();
        var now = DateTime.UtcNow;
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            "us1", 42, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromHours(1));
        factory.AuthBridgeClient.NextAccessLevel = 5;

        using var client = factory.CreateClient();

        for (var i = 1; i <= 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/whoami");
            request.Headers.Add("Cookie", $"ace_cloud_session={secret.Secret}");
            using var response = await client.SendAsync(request);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(i, factory.AuthBridgeClient.AccessLevelCallCount, "ADM-001 requires a fresh check on every sensitive request, not a cached one.");
        }
    }
}
