using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// Issue #33's Red -> Green coverage for account identity, Main/Linked display, and destructive
/// account-linking (AUTH-003, AUTH-004, AUTH-005..009): "no linked credential can view or mutate
/// Main assets," and "every blocked link reason has an exact response."
/// </summary>
[TestClass]
public sealed class AccountIdentityEndpointsTests
{
    private const uint MainAccountId = 42;
    private const uint LinkedAccountId = 43;
    private const string ShardId = "us1";

    private static readonly CloudPrivateServiceKeyRing SharedKeyRing = new(
        new CloudPrivateServiceKey("test-active-key", Convert.FromBase64String("dGVzdC1hY3RpdmUtc2VjcmV0LTMyLWJ5dGVzLWxvbmc=")));

    private static string ValidGrantFor(uint accountId, DateTime nowUtc) =>
        CloudAuthGrantIssuer.Issue(accountId, CloudAuthAudiences.CloudBackend, nowUtc, TimeSpan.FromSeconds(30), SharedKeyRing);

    private static void UseAllowedOrigin(HttpRequestMessage request) =>
        request.Headers.Add("Origin", BackendTestFactory.AllowedOrigin);

    [TestMethod]
    public async Task GetIdentity_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/account/identity");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetIdentity_StandaloneAccount_ReturnsMainKindAndNoLinkedAccounts()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await client.GetAsync("/account/identity");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.AreEqual("Main", body!["accountKind"]!.GetValue<string>());
        Assert.AreEqual(MainAccountId, body["mainAccountId"]!.GetValue<uint>());
        Assert.IsEmpty(body["linkedAccounts"]!.AsArray());
    }

    [TestMethod]
    public async Task GetIdentity_LinkedAccountSession_ReturnsLinkedKind_AndNeverListsGroupMembers()
    {
        // AUTH-004: a Linked Account's own credentials can never see who else shares the group.
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(LinkedAccountId, MainAccountId);
        factory.AccountLinkAdministration.SeedActiveLink(MainAccountId, LinkedAccountId, DateTime.UtcNow);

        using var client = await AuthenticatedClientAsync(factory, LinkedAccountId);

        using var response = await client.GetAsync("/account/identity");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.AreEqual("Linked", body!["accountKind"]!.GetValue<string>());
        Assert.AreEqual(MainAccountId, body["mainAccountId"]!.GetValue<uint>());
        Assert.IsEmpty(body["linkedAccounts"]!.AsArray());
    }

    [TestMethod]
    public async Task GetIdentity_MainWithLinkedAccounts_ListsThem()
    {
        await using var factory = new BackendTestFactory();
        var linkedAtUtc = DateTime.UtcNow;
        factory.AccountLinkAdministration.SeedActiveLink(MainAccountId, LinkedAccountId, linkedAtUtc);

        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await client.GetAsync("/account/identity");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var links = body!["linkedAccounts"]!.AsArray();
        Assert.HasCount(1, links);
        Assert.AreEqual(LinkedAccountId, links[0]!["accountId"]!.GetValue<uint>());
    }

    [TestMethod]
    public async Task Link_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/link")
        {
            Content = JsonContent.Create(new CloudAccountLinkRequestBody("mule1", "hunter2")),
        };
        UseAllowedOrigin(request);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Link_LinkedAccountSession_ReturnsForbidden()
    {
        // AUTH-004 enforced server-side: a Linked Account's own credentials cannot mutate Main assets,
        // including creating a new link, even if the web client's own route guard is bypassed.
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(LinkedAccountId, MainAccountId);

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, LinkedAccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/link")
        {
            Content = JsonContent.Create(new CloudAccountLinkRequestBody("mule1", "hunter2")),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        client.Dispose();
    }

    [TestMethod]
    public async Task Link_WithoutCsrfToken_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, MainAccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/link")
        {
            Content = JsonContent.Create(new CloudAccountLinkRequestBody("mule1", "hunter2")),
        };
        UseAllowedOrigin(request);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Link_InvalidSourcePassword_ReturnsUnauthorized_NeverDistinguishingReason()
    {
        // The delayed-confirmation warning and destructive-action UX live entirely in the web
        // client; the server's own safeguard is requiring the *source* account's own password.
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.InvalidCredentials, Grant: null);

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/link")
        {
            Content = JsonContent.Create(new CloudAccountLinkRequestBody("mule1", "wrong")),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        client.Dispose();
    }

    [TestMethod]
    public async Task Link_AuthBridgeUnavailable_ReturnsServiceUnavailable()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Unavailable, Grant: null);

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/link")
        {
            Content = JsonContent.Create(new CloudAccountLinkRequestBody("mule1", "hunter2")),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        client.Dispose();
    }

    [TestMethod]
    public async Task Link_ValidSourcePassword_ApprovesAndUsesTheGrantsAccountId_NeverTheTypedName()
    {
        await using var factory = new BackendTestFactory();
        var now = DateTime.UtcNow;
        const uint sourceAccountId = 99;
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Issued, ValidGrantFor(sourceAccountId, now));

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/link")
        {
            Content = JsonContent.Create(new CloudAccountLinkRequestBody("mule1", "hunter2")),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsTrue(body!["approved"]!.GetValue<bool>());
        Assert.AreEqual(sourceAccountId, factory.AccountLinkAdministration.LastLinkedSourceAccountId);
        client.Dispose();
    }

    [TestMethod]
    public async Task Link_PolicyRejects_ReturnsExactRejectionCode()
    {
        await using var factory = new BackendTestFactory();
        var now = DateTime.UtcNow;
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Issued, ValidGrantFor(99, now));
        factory.AccountLinkAdministration.NextLinkRejectionCode = CloudAccountLinkRejectionCode.SourceHasPendingObligations;

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/link")
        {
            Content = JsonContent.Create(new CloudAccountLinkRequestBody("mule1", "hunter2")),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsFalse(body!["approved"]!.GetValue<bool>());
        Assert.AreEqual("SourceHasPendingObligations", body["rejectionCode"]!.GetValue<string>());
        client.Dispose();
    }

    [TestMethod]
    public async Task Unlink_LinkedAccountSession_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(LinkedAccountId, MainAccountId);

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, LinkedAccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/unlink")
        {
            Content = JsonContent.Create(new CloudAccountUnlinkRequestBody(LinkedAccountId)),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        client.Dispose();
    }

    [TestMethod]
    public async Task Unlink_Approved_RemovesTheLinkFromTheIdentityView()
    {
        await using var factory = new BackendTestFactory();
        factory.AccountLinkAdministration.SeedActiveLink(MainAccountId, LinkedAccountId, DateTime.UtcNow);

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/account/unlink")
        {
            Content = JsonContent.Create(new CloudAccountUnlinkRequestBody(LinkedAccountId)),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsTrue(body!["approved"]!.GetValue<bool>());

        using var identityResponse = await client.GetAsync("/account/identity");
        var identity = await identityResponse.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsEmpty(identity!["linkedAccounts"]!.AsArray());
        client.Dispose();
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(BackendTestFactory factory, uint accountId)
    {
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, accountId, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), DateTime.UtcNow, TimeSpan.FromHours(1));

        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        return client;
    }

    private static async Task<(HttpClient Client, string CsrfToken)> AuthenticatedClientWithCsrfAsync(BackendTestFactory factory, uint accountId)
    {
        var secret = CloudWebSessionSecretHasher.Generate();
        var csrfToken = CloudCsrfTokenGenerator.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, accountId, Guid.NewGuid(), secret.Hash, csrfToken, DateTime.UtcNow, TimeSpan.FromHours(1));

        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        return (client, csrfToken);
    }
}
