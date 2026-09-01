using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;

namespace ACE.Cloud.Backend.Tests;

/// <summary>Issue #39 Red -> Green endpoint coverage for the Transfer Offer web surface (XFER-001, XFER-002).</summary>
[TestClass]
public sealed class CloudTransferOfferEndpointsTests
{
    private const uint SenderAccountId = 42;
    private const uint RecipientAccountId = 43;
    private const string ShardId = "us1";

    [TestMethod]
    public async Task List_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/transfer-offers");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Create_ThenList_AppearsInSendersSentAndRecipientsReceived()
    {
        await using var factory = new BackendTestFactory();
        var recipientOwnerId = CloudOwnerIdentity.ForAccount(ShardId, RecipientAccountId);
        factory.TransferOfferService.RecipientOwnerIdsByCharacterName["Recipient"] = recipientOwnerId;

        using var senderClient = await AuthenticatedClientAsync(factory, SenderAccountId);
        using var createResponse = await senderClient.PostAsJsonAsync("/transfer-offers", new
        {
            recipientCharacterName = "Recipient",
            targets = new[] { new { kind = "Item", itemBiotaId = 123u, stackLotId = (Guid?)null } },
        });

        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);

        using var sentResponse = await senderClient.GetAsync("/transfer-offers");
        var sentBody = await sentResponse.Content.ReadFromJsonAsync<JsonNode>();
        Assert.HasCount(1, sentBody!["sent"]!.AsArray());
        Assert.HasCount(0, sentBody["received"]!.AsArray());

        using var recipientClient = await AuthenticatedClientAsync(factory, RecipientAccountId);
        using var receivedResponse = await recipientClient.GetAsync("/transfer-offers");
        var receivedBody = await receivedResponse.Content.ReadFromJsonAsync<JsonNode>();
        Assert.HasCount(1, receivedBody!["received"]!.AsArray());
        Assert.HasCount(0, receivedBody["sent"]!.AsArray());
    }

    [TestMethod]
    public async Task Create_UnknownRecipientCharacter_ReturnsConflict()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, SenderAccountId);

        using var response = await client.PostAsJsonAsync("/transfer-offers", new
        {
            recipientCharacterName = "NoSuchCharacter",
            targets = new[] { new { kind = "Item", itemBiotaId = 123u, stackLotId = (Guid?)null } },
        });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task Create_NoTargets_ReturnsBadRequest()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, SenderAccountId);

        using var response = await client.PostAsJsonAsync("/transfer-offers", new { recipientCharacterName = "Recipient", targets = Array.Empty<object>() });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Create_WithoutCsrfHeader_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.TransferOfferService.RecipientOwnerIdsByCharacterName["Recipient"] = CloudOwnerIdentity.ForAccount(ShardId, RecipientAccountId);

        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, SenderAccountId, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), DateTime.UtcNow, TimeSpan.FromHours(1));
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);

        using var response = await client.PostAsJsonAsync("/transfer-offers", new
        {
            recipientCharacterName = "Recipient",
            targets = new[] { new { kind = "Item", itemBiotaId = 123u, stackLotId = (Guid?)null } },
        });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Accept_ByAnUnrelatedAccount_ReturnsConflict()
    {
        await using var factory = new BackendTestFactory();
        factory.TransferOfferService.RecipientOwnerIdsByCharacterName["Recipient"] = CloudOwnerIdentity.ForAccount(ShardId, RecipientAccountId);

        using var senderClient = await AuthenticatedClientAsync(factory, SenderAccountId);
        var createResponse = await senderClient.PostAsJsonAsync("/transfer-offers", new
        {
            recipientCharacterName = "Recipient",
            targets = new[] { new { kind = "Item", itemBiotaId = 123u, stackLotId = (Guid?)null } },
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonNode>();
        var offerId = created!["id"]!.GetValue<Guid>();
        var version = created["version"]!.GetValue<int>();

        using var unrelatedClient = await AuthenticatedClientAsync(factory, 999);
        using var acceptResponse = await unrelatedClient.PostAsJsonAsync($"/transfer-offers/{offerId}/accept", new { expectedVersion = version });

        Assert.AreEqual(HttpStatusCode.Conflict, acceptResponse.StatusCode);
    }

    [TestMethod]
    public async Task Accept_ByTheRecipient_Succeeds()
    {
        await using var factory = new BackendTestFactory();
        factory.TransferOfferService.RecipientOwnerIdsByCharacterName["Recipient"] = CloudOwnerIdentity.ForAccount(ShardId, RecipientAccountId);

        using var senderClient = await AuthenticatedClientAsync(factory, SenderAccountId);
        var createResponse = await senderClient.PostAsJsonAsync("/transfer-offers", new
        {
            recipientCharacterName = "Recipient",
            targets = new[] { new { kind = "Item", itemBiotaId = 123u, stackLotId = (Guid?)null } },
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonNode>();
        var offerId = created!["id"]!.GetValue<Guid>();
        var version = created["version"]!.GetValue<int>();

        using var recipientClient = await AuthenticatedClientAsync(factory, RecipientAccountId);
        using var acceptResponse = await recipientClient.PostAsJsonAsync($"/transfer-offers/{offerId}/accept", new { expectedVersion = version });

        Assert.AreEqual(HttpStatusCode.OK, acceptResponse.StatusCode);
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
