using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Contracts;
using ACE.Cloud.Domain;
using ACE.Entity.Enum;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// Issue #31 Red -> Green endpoint coverage for the HTTP surface over #30's shared inventory query
/// contract, the Full Cloud Appraisal panel, and composed icon derivatives.
/// </summary>
[TestClass]
public sealed class CloudInventoryEndpointsTests
{
    private const uint AccountId = 42;
    private const string ShardId = "us1";

    [TestMethod]
    public async Task QueryPages_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/inventory/pages");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task QueryPages_LinkedAccountSession_ReturnsForbidden()
    {
        // AUTH-004: "Linked credentials... cannot view or mutate Main assets," enforced server-side.
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(AccountId, mainAccountId: 99);
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/inventory/pages");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task QueryPages_ZeroOrNegativePage_ReturnsBadRequest()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/inventory/pages?page=0");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task QueryPages_OwnAccount_ReturnsOnlyThatOwnersItems()
    {
        await using var factory = new BackendTestFactory();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        SeedWholeItem(factory, ownerId, "Ivory Buckler", CloudInventoryCategory.Armor);
        SeedWholeItem(factory, Guid.NewGuid(), "Someone Else's Sword", CloudInventoryCategory.MeleeWeapons);

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/inventory/pages?category=Armor");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var items = body!["page"]!["items"]!.AsArray();
        Assert.HasCount(1, items);
        Assert.AreEqual("Ivory Buckler", items[0]!["name"]!.GetValue<string>());
        // Every enum on the wire is a stable string, never a raw C# int (CloudDiagnosticsEndpoints'
        // existing convention) -- a future domain reorder must not silently renumber this field.
        Assert.AreEqual("Armor", items[0]!["category"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task GetAppraisal_ZeroItemId_ReturnsBadRequest()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/inventory/items/0/appraisal");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAppraisal_ItemNotVisibleToViewer_ReturnsNotFound()
    {
        await using var factory = new BackendTestFactory();
        SeedWholeItem(factory, Guid.NewGuid(), "Someone Else's Sword", CloudInventoryCategory.MeleeWeapons, biotaId: 123456);

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/inventory/items/123456/appraisal");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetAppraisal_OwnedItem_ReturnsACompleteCharacterIndependentPanel()
    {
        await using var factory = new BackendTestFactory();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        SeedWholeItem(factory, ownerId, "Ivory Buckler", CloudInventoryCategory.Armor, biotaId: 777, value: 100, burden: 20);

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/inventory/items/777/appraisal");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var panel = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.AreEqual("Ivory Buckler", panel!["itemName"]!.GetValue<string>());
        var sections = panel["sections"]!.AsArray();
        Assert.IsNotEmpty(sections);
        // Every enum on the wire is a stable string, never a raw C# int.
        Assert.AreEqual("Header", sections[0]!["kind"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task GetIcon_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/inventory/icons/{new string('a', 64)}");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetIcon_MalformedCacheKey_ReturnsBadRequest()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/inventory/icons/not-a-real-key");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task GetIcon_NoComposedDerivative_ReturnsNotFound()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync($"/inventory/icons/{new string('b', 64)}");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetIcon_ComposedDerivativeExists_ReturnsItsPngBytesWithLongLivedCaching()
    {
        await using var factory = new BackendTestFactory();
        var hex = new string('c', 64);
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        factory.IconDerivativeReader.Seed(hex, pngBytes);

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync($"/inventory/icons/{hex}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("image/png", response.Content.Headers.ContentType?.MediaType);
        CollectionAssert.AreEqual(pngBytes, await response.Content.ReadAsByteArrayAsync());
        StringAssert.Contains(response.Headers.CacheControl!.ToString(), "immutable");
    }

    private static void SeedWholeItem(
        BackendTestFactory factory, Guid ownerId, string name, CloudInventoryCategory category, uint biotaId = 1, int? value = null, int? burden = null)
    {
        factory.InventoryQueryReader.Candidates.Add(new CloudInventoryQueryCandidate(
            new CloudItemId(biotaId), StackLotId: null, ownerId, name, category, Quantity: 1, value, burden,
            IsReserved: false, new CloudAggregateVersion(1)));
        factory.InventoryItemPropertiesGateway.Seed(biotaId, ShardId, name, value, burden);
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
}
