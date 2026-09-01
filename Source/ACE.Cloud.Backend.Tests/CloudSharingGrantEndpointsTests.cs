using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;

namespace ACE.Cloud.Backend.Tests;

/// <summary>Issue #39 Red -> Green endpoint coverage for the personal Sharing Grant web surface (SHARE-001..004).</summary>
[TestClass]
public sealed class CloudSharingGrantEndpointsTests
{
    private const uint OwnerAccountId = 42;
    private const uint GranteeAccountId = 43;
    private const string ShardId = "us1";

    [TestMethod]
    public async Task List_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/sharing-grants");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Set_ThenList_AppearsInOwnersGivenAndGranteesReceived()
    {
        await using var factory = new BackendTestFactory();
        factory.SharingGrantService.GranteeOwnerIdsByCharacterName["Grantee"] = CloudOwnerIdentity.ForAccount(ShardId, GranteeAccountId);

        using var ownerClient = await AuthenticatedClientAsync(factory, OwnerAccountId);
        using var setResponse = await ownerClient.PostAsJsonAsync("/sharing-grants", new { granteeCharacterName = "Grantee", level = "ViewOnly" });

        Assert.AreEqual(HttpStatusCode.OK, setResponse.StatusCode);

        using var givenResponse = await ownerClient.GetAsync("/sharing-grants");
        var givenBody = await givenResponse.Content.ReadFromJsonAsync<JsonNode>();
        Assert.HasCount(1, givenBody!["given"]!.AsArray());
        Assert.AreEqual("ViewOnly", givenBody["given"]![0]!["level"]!.GetValue<string>());

        using var granteeClient = await AuthenticatedClientAsync(factory, GranteeAccountId);
        using var receivedResponse = await granteeClient.GetAsync("/sharing-grants");
        var receivedBody = await receivedResponse.Content.ReadFromJsonAsync<JsonNode>();
        Assert.HasCount(1, receivedBody!["received"]!.AsArray());
    }

    [TestMethod]
    public async Task Set_ExplicitNone_OverwritesTheExistingGrant_NotAppendingASecondRow()
    {
        await using var factory = new BackendTestFactory();
        factory.SharingGrantService.GranteeOwnerIdsByCharacterName["Grantee"] = CloudOwnerIdentity.ForAccount(ShardId, GranteeAccountId);

        using var client = await AuthenticatedClientAsync(factory, OwnerAccountId);
        await client.PostAsJsonAsync("/sharing-grants", new { granteeCharacterName = "Grantee", level = "ViewAndWithdraw" });
        using var response = await client.PostAsJsonAsync("/sharing-grants", new { granteeCharacterName = "Grantee", level = "None" });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var listResponse = await client.GetAsync("/sharing-grants");
        var body = await listResponse.Content.ReadFromJsonAsync<JsonNode>();
        var given = body!["given"]!.AsArray();
        Assert.HasCount(1, given, "SHARE-004: an explicit None re-send must remain one auditable upsert row, never a second grant.");
        Assert.AreEqual("None", given[0]!["level"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Set_UnknownGranteeCharacter_ReturnsConflict()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, OwnerAccountId);

        using var response = await client.PostAsJsonAsync("/sharing-grants", new { granteeCharacterName = "NoSuchCharacter", level = "ViewOnly" });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task Set_InvalidLevel_ReturnsBadRequest()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, OwnerAccountId);

        using var response = await client.PostAsJsonAsync("/sharing-grants", new { granteeCharacterName = "Grantee", level = "SuperAdmin" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Set_WithoutCsrfHeader_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, OwnerAccountId, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), DateTime.UtcNow, TimeSpan.FromHours(1));
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);

        using var response = await client.PostAsJsonAsync("/sharing-grants", new { granteeCharacterName = "Grantee", level = "ViewOnly" });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(BackendTestFactory factory, uint accountId)
    {
        var secret = CloudWebSessionSecretHasher.Generate();
        var csrfToken = CloudCsrfTokenGenerator.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, accountId, Guid.NewGuid(), secret.Hash, csrfToken, DateTime.UtcNow, TimeSpan.FromHours(1));

        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        client.DefaultRequestHeaders.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);
        return client;
    }
}
