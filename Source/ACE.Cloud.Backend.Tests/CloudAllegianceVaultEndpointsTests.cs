using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>Issue #39 Red -> Green endpoint coverage for the Allegiance Vault web surface (VAULT-001..003).</summary>
[TestClass]
public sealed class CloudAllegianceVaultEndpointsTests
{
    private const uint AccountId = 42;
    private const uint CharacterId = 7001;
    private const uint MonarchId = 9001;
    private const string ShardId = "us1";

    [TestMethod]
    public async Task ListActingCharacters_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/allegiance-vault/acting-characters");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListActingCharacters_ReturnsTheAccountsCurrentCharactersWithMonarch()
    {
        await using var factory = new BackendTestFactory();
        factory.ActingCharacterReader.SetCharacters(AccountId, new CloudActingCharacterSummary(CharacterId, "Vassal", MonarchId));

        using var client = await AuthenticatedClientAsync(factory, AccountId);
        using var response = await client.GetAsync("/allegiance-vault/acting-characters");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var characters = body!["characters"]!.AsArray();
        Assert.HasCount(1, characters);
        Assert.AreEqual("Vassal", characters[0]!["characterName"]!.GetValue<string>());
        Assert.IsTrue(characters[0]!["hasAllegiance"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task GetVault_ForACharacterNotOwnedByTheCaller_ReturnsNotFound()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync($"/allegiance-vault?characterId={CharacterId}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetVault_ForAnOwnedCurrentAllegianceCharacter_OnlyReturnsItemsOwnedByThatVault()
    {
        await using var factory = new BackendTestFactory();
        factory.ActingCharacterReader.SetCharacters(AccountId, new CloudActingCharacterSummary(CharacterId, "Vassal", MonarchId));
        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, MonarchId);

        factory.InventoryQueryReader.Candidates.Add(new ACE.Cloud.Domain.CloudInventoryQueryCandidate(
            new CloudItemId(555), StackLotId: null, vaultOwnerId, "Vault Sword", ACE.Cloud.Domain.CloudInventoryCategory.MeleeWeapons,
            Quantity: 1, Value: 10, Burden: 5, IsReserved: false, new ACE.Cloud.Domain.CloudAggregateVersion(1)));
        factory.InventoryQueryReader.Candidates.Add(new ACE.Cloud.Domain.CloudInventoryQueryCandidate(
            new CloudItemId(556), StackLotId: null, Guid.NewGuid(), "Someone Else's Sword", ACE.Cloud.Domain.CloudInventoryCategory.MeleeWeapons,
            Quantity: 1, Value: 10, Burden: 5, IsReserved: false, new ACE.Cloud.Domain.CloudAggregateVersion(1)));

        using var client = await AuthenticatedClientAsync(factory, AccountId);
        using var response = await client.GetAsync($"/allegiance-vault?characterId={CharacterId}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var items = body!["page"]!["items"]!.AsArray();
        Assert.HasCount(1, items, "The vault view must be scoped to this vault's own owner ID, never another owner's items.");
        Assert.AreEqual("Vault Sword", items[0]!["name"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Contribute_ByACharacterWithNoAllegiance_ReturnsConflict()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync("/allegiance-vault/contribute", new
        {
            actingCharacterId = CharacterId,
            kind = "Item",
            itemBiotaId = 555u,
            stackLotId = (Guid?)null,
        });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task ContributeThenTake_RoundTripsTheItemBetweenPersonalAndVaultOwnership()
    {
        await using var factory = new BackendTestFactory();
        factory.AllegianceVaultTransactionService.SetCharacterMonarch(CharacterId, MonarchId);
        var personalOwnerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        factory.AllegianceVaultTransactionService.SeedPersonalItem(555u, personalOwnerId);

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var contributeResponse = await client.PostAsJsonAsync("/allegiance-vault/contribute", new
        {
            actingCharacterId = CharacterId,
            kind = "Item",
            itemBiotaId = 555u,
            stackLotId = (Guid?)null,
        });
        Assert.AreEqual(HttpStatusCode.OK, contributeResponse.StatusCode);

        using var takeResponse = await client.PostAsJsonAsync("/allegiance-vault/take", new
        {
            actingCharacterId = CharacterId,
            kind = "Item",
            itemBiotaId = 555u,
            stackLotId = (Guid?)null,
        });
        Assert.AreEqual(HttpStatusCode.OK, takeResponse.StatusCode);
    }

    [TestMethod]
    public async Task Contribute_WithoutCsrfHeader_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, AccountId, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), DateTime.UtcNow, TimeSpan.FromHours(1));
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);

        using var response = await client.PostAsJsonAsync("/allegiance-vault/contribute", new
        {
            actingCharacterId = CharacterId,
            kind = "Item",
            itemBiotaId = 555u,
            stackLotId = (Guid?)null,
        });

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
