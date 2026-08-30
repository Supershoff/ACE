using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;

namespace ACE.Cloud.Backend.Tests;

/// <summary>Issue #34 Red -> Green endpoint coverage for the scoped Activity Ledger HTTP surface.</summary>
[TestClass]
public sealed class CloudActivityLedgerEndpointsTests
{
    private const uint AccountId = 42;
    private const string ShardId = "us1";

    [TestMethod]
    public async Task Query_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/activity");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Query_OwnerScope_SeesOnlyItsOwnEntries()
    {
        await using var factory = new BackendTestFactory();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        SeedCustodyEntry(factory, ownerId, "Deposit");
        SeedCustodyEntry(factory, Guid.NewGuid(), "Deposit");

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/activity");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var entries = body!["entries"]!.AsArray();
        Assert.HasCount(1, entries);
        Assert.AreEqual("CustodyBoundary", entries[0]!["category"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Query_VaultScope_IncludesTheAccountsAllegianceVaultOwner()
    {
        await using var factory = new BackendTestFactory();
        var vaultOwnerId = Guid.NewGuid();
        factory.CharacterAllegianceVaultReader.SetVaultOwnerIds(AccountId, vaultOwnerId);
        SeedCustodyEntry(factory, vaultOwnerId, "VaultAbsorption");

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/activity?vault=true");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var entries = body!["entries"]!.AsArray();
        Assert.HasCount(1, entries);
    }

    [TestMethod]
    public async Task Query_WithoutVaultFlag_NeverSeesAnotherOwnersAllegianceVaultEntry()
    {
        await using var factory = new BackendTestFactory();
        var vaultOwnerId = Guid.NewGuid();
        factory.CharacterAllegianceVaultReader.SetVaultOwnerIds(AccountId, vaultOwnerId);
        SeedCustodyEntry(factory, vaultOwnerId, "VaultAbsorption");

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/activity");

        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.HasCount(0, body!["entries"]!.AsArray());
    }

    [TestMethod]
    public async Task Query_AdminSession_SeesAdminOnlyCategoriesToo()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 5;
        SeedAccountLinkEntry(factory);

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/activity");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var entries = body!["entries"]!.AsArray();
        Assert.HasCount(1, entries);
        Assert.AreEqual("AccountLink", entries[0]!["category"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Query_NonAdminSession_NeverSeesAnAdminOnlyCategory()
    {
        await using var factory = new BackendTestFactory();
        SeedAccountLinkEntry(factory);

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/activity");

        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.HasCount(0, body!["entries"]!.AsArray());
    }

    [TestMethod]
    public async Task Query_ZeroOrNegativePage_ReturnsBadRequest()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/activity?page=0");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Query_EveryEntry_CarriesItsOwnCorrelationId()
    {
        await using var factory = new BackendTestFactory();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        var correlationId = Guid.NewGuid();
        factory.ActivityLedgerQueryReader.Candidates.Add(new CloudActivityLedgerEntry(
            Guid.NewGuid(), correlationId, ShardId, CloudActivityLedgerCategory.CustodyBoundary,
            "Deposit", ownerId, 123, "Committed", null, DateTime.UtcNow));

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/activity");

        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.AreEqual(correlationId.ToString(), body!["entries"]![0]!["correlationId"]!.GetValue<string>());
    }

    private static void SeedCustodyEntry(BackendTestFactory factory, Guid ownerId, string eventType) =>
        factory.ActivityLedgerQueryReader.Candidates.Add(new CloudActivityLedgerEntry(
            Guid.NewGuid(), Guid.NewGuid(), ShardId, CloudActivityLedgerCategory.CustodyBoundary,
            eventType, ownerId, 123, "Committed", null, DateTime.UtcNow));

    private static void SeedAccountLinkEntry(BackendTestFactory factory) =>
        factory.ActivityLedgerQueryReader.Candidates.Add(new CloudActivityLedgerEntry(
            Guid.NewGuid(), Guid.NewGuid(), ShardId, CloudActivityLedgerCategory.AccountLink,
            "Linked", OwnerId: null, ItemBiotaId: null, Outcome: null, Reason: null, DateTime.UtcNow));

    private static async Task<HttpClient> AuthenticatedClientAsync(BackendTestFactory factory, uint accountId)
    {
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, accountId, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), DateTime.UtcNow, TimeSpan.FromHours(1));

        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        return client;
    }
}
