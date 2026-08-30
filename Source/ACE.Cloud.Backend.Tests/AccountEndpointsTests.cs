using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// Issue #33's Red -> Green endpoint coverage for account/display settings and destructive account
/// linking (AUTH-003..009).
/// </summary>
[TestClass]
public sealed class AccountEndpointsTests
{
    private const uint MainAccountId = 42;
    private const uint SourceAccountId = 77;
    private const string ShardId = "us1";

    private static readonly CloudPrivateServiceKeyRing SharedKeyRing = new(
        new CloudPrivateServiceKey("test-active-key", Convert.FromBase64String("dGVzdC1hY3RpdmUtc2VjcmV0LTMyLWJ5dGVzLWxvbmc=")));

    [TestMethod]
    public async Task GetOverview_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/account/overview");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetOverview_LinkedAccountSession_RevealsOnlyThatItIsLinked()
    {
        // AUTH-004: "web login shows only that they are linked; it cannot view or mutate Main assets."
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(SourceAccountId, MainAccountId);
        using var client = await AuthenticatedClientAsync(factory, SourceAccountId);

        using var response = await client.GetAsync("/account/overview");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsTrue(body!["isLinkedAccount"]!.GetValue<bool>());
        Assert.IsNull(body["mainAccountId"]);
        Assert.IsNull(body["linkedAccountIds"]);
    }

    [TestMethod]
    public async Task GetOverview_MainAccountWithNoLinks_ReturnsEmptyLinkedAccountsAndNoDisplayCharacter()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await client.GetAsync("/account/overview");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsFalse(body!["isLinkedAccount"]!.GetValue<bool>());
        Assert.AreEqual(MainAccountId, body["mainAccountId"]!.GetValue<uint>());
        Assert.HasCount(0, body["linkedAccountIds"]!.AsArray());
        Assert.IsNull(body["displayCharacter"]);
    }

    [TestMethod]
    public async Task GetOverview_MainAccountWithLinksAndSelection_ReturnsBothWithoutLeakingAccountNames()
    {
        await using var factory = new BackendTestFactory();
        var groupId = Guid.NewGuid();
        factory.AccountOwnershipResolver.SetOwnershipGroup(MainAccountId, groupId, SourceAccountId);
        factory.DisplayCharacterGateway.Seed(groupId, ShardId, characterId: 900, characterName: "Bob", totalLogins: 12);

        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await client.GetAsync("/account/overview");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var linkedIds = body!["linkedAccountIds"]!.AsArray();
        Assert.HasCount(1, linkedIds);
        Assert.AreEqual(SourceAccountId, linkedIds[0]!.GetValue<uint>());
        Assert.AreEqual("Bob", body["displayCharacter"]!["characterName"]!.GetValue<string>());
        // No raw account name field appears anywhere in the payload.
        Assert.IsNull(body["sourceAccountName"]);
        Assert.IsNull(body["mainAccountName"]);
    }

    [TestMethod]
    public async Task Link_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);

        using var response = await PostJsonAsync(client, "/account/link", new AccountLinkRequest(SourceAccountId.ToString(), "pw"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Link_WithoutCsrfToken_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, MainAccountId, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), DateTime.UtcNow, TimeSpan.FromHours(1));
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);

        using var response = await PostJsonAsync(client, "/account/link", new AccountLinkRequest("sourceAccount", "hunter2"));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Link_MissingFields_ReturnsBadRequest()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await PostJsonAsync(client, "/account/link", new AccountLinkRequest("", ""));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Link_LinkedAccountSession_ReturnsForbidden()
    {
        // AUTH-004: only Main Account credentials may initiate a link.
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(SourceAccountId, MainAccountId);
        using var client = await AuthenticatedClientAsync(factory, SourceAccountId);

        using var response = await PostJsonAsync(client, "/account/link", new AccountLinkRequest("someAccount", "pw"));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Link_WrongSourcePassword_ReturnsUnauthorizedAndNeverCallsTheLinkGateway()
    {
        // AUTH-007: source-password re-entry, verified through the private ACE Auth Bridge.
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.InvalidCredentials, Grant: null);
        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await PostJsonAsync(client, "/account/link", new AccountLinkRequest("sourceAccount", "wrong-password"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.HasCount(0, factory.AccountOwnershipResolver.LinkCalls);
    }

    [TestMethod]
    public async Task Link_ValidSourceCredentials_CallsTheLinkGatewayWithBothAccountsAndReselectsDisplayCharacter()
    {
        await using var factory = new BackendTestFactory();
        SeedValidSourceGrant(factory, SourceAccountId);
        factory.AccountOwnershipResolver.NextLinkOutcome = CloudAccountLinkOutcome.Approved(Guid.NewGuid(), Guid.NewGuid());
        factory.AccountOwnershipResolver.SetOwnershipGroup(MainAccountId, Guid.NewGuid(), SourceAccountId);
        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await PostJsonAsync(client, "/account/link", new AccountLinkRequest("sourceAccount", "correct-password"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsTrue(body!["approved"]!.GetValue<bool>());
        Assert.HasCount(1, factory.AccountOwnershipResolver.LinkCalls);
        Assert.AreEqual((MainAccountId, SourceAccountId), factory.AccountOwnershipResolver.LinkCalls[0]);
        // AUTH-003: a link changes the group's roster, so Display Character reselection runs.
        Assert.HasCount(1, factory.DisplayCharacterGateway.ReselectCalls);
    }

    [TestMethod]
    public async Task Link_GatewayRejectsTheRequest_ReturnsConflictWithTheExactReason()
    {
        await using var factory = new BackendTestFactory();
        SeedValidSourceGrant(factory, SourceAccountId);
        factory.AccountOwnershipResolver.NextLinkOutcome = CloudAccountLinkOutcome.Rejected(CloudAccountLinkRejectionCode.SourceHasPendingObligations);
        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await PostJsonAsync(client, "/account/link", new AccountLinkRequest("sourceAccount", "correct-password"));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.AreEqual("SourceHasPendingObligations", body!["reason"]!.GetValue<string>());
        // A rejected link never changed the roster, so no reselection happens.
        Assert.HasCount(0, factory.DisplayCharacterGateway.ReselectCalls);
    }

    [TestMethod]
    public async Task Unlink_LinkedAccountSession_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(SourceAccountId, MainAccountId);
        using var client = await AuthenticatedClientAsync(factory, SourceAccountId);

        using var response = await PostJsonAsync(client, "/account/unlink", new AccountUnlinkRequest(SourceAccountId));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Unlink_Approved_ReturnsOkAndReselectsDisplayCharacter()
    {
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.NextUnlinkOutcome = CloudAccountLinkOutcome.Approved(Guid.NewGuid(), Guid.NewGuid());
        factory.AccountOwnershipResolver.SetOwnershipGroup(MainAccountId, Guid.NewGuid());
        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await PostJsonAsync(client, "/account/unlink", new AccountUnlinkRequest(SourceAccountId));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(1, factory.AccountOwnershipResolver.UnlinkCalls);
        Assert.HasCount(1, factory.DisplayCharacterGateway.ReselectCalls);
    }

    private static void SeedValidSourceGrant(BackendTestFactory factory, uint sourceAccountId)
    {
        var grant = CloudAuthGrantIssuer.Issue(sourceAccountId, CloudAuthAudiences.CloudBackend, DateTime.UtcNow, TimeSpan.FromSeconds(30), SharedKeyRing);
        factory.AuthBridgeClient.NextGrantResult = new CloudAuthBridgeGrantResult(CloudAuthBridgeGrantOutcomeKind.Issued, grant);
    }

    private static Task<HttpResponseMessage> PostJsonAsync<TBody>(HttpClient client, string path, TBody body) =>
        client.PostAsJsonAsync(path, body);

    private static async Task<HttpClient> AuthenticatedClientAsync(BackendTestFactory factory, uint accountId)
    {
        var secret = CloudWebSessionSecretHasher.Generate();
        var csrfToken = CloudCsrfTokenGenerator.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, accountId, Guid.NewGuid(), secret.Hash, csrfToken, DateTime.UtcNow, TimeSpan.FromHours(1));

        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);
        client.DefaultRequestHeaders.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);
        return client;
    }
}
