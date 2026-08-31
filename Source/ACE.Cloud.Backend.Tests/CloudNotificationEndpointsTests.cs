using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;

namespace ACE.Cloud.Backend.Tests;

/// <summary>Issue #34 Red -> Green endpoint coverage for the Notification Center HTTP surface.</summary>
[TestClass]
public sealed class CloudNotificationEndpointsTests
{
    private const uint AccountId = 42;
    private const string ShardId = "us1";

    [TestMethod]
    public async Task List_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/notifications");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task List_OnlyReturnsTheAuthenticatedOwnersNotifications()
    {
        await using var factory = new BackendTestFactory();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        var mine = new CloudNotificationSnapshot(
            Guid.NewGuid(), CloudNotificationKind.OwnershipReceived, "/dashboard", 1, false, DateTime.UtcNow, DateTime.UtcNow);
        factory.NotificationGateway.Seed(ownerId, mine);
        factory.NotificationGateway.Seed(Guid.NewGuid(), new CloudNotificationSnapshot(
            Guid.NewGuid(), CloudNotificationKind.OwnershipReceived, "/dashboard", 1, false, DateTime.UtcNow, DateTime.UtcNow));

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/notifications");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var notifications = body!["notifications"]!.AsArray();
        Assert.HasCount(1, notifications);
        Assert.AreEqual(mine.Id.ToString(), notifications[0]!["id"]!.GetValue<string>());
        Assert.AreEqual(1, notifications[0]!["count"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task UnreadCount_CountsOnlyUnreadNotifications()
    {
        await using var factory = new BackendTestFactory();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        factory.NotificationGateway.Seed(ownerId, new CloudNotificationSnapshot(
            Guid.NewGuid(), CloudNotificationKind.OwnershipReceived, "/dashboard", 1, false, DateTime.UtcNow, DateTime.UtcNow));
        factory.NotificationGateway.Seed(ownerId, new CloudNotificationSnapshot(
            Guid.NewGuid(), CloudNotificationKind.OwnershipReceived, "/dashboard", 1, true, DateTime.UtcNow, DateTime.UtcNow));

        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/notifications/unread-count");

        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.AreEqual(1, body!["unreadCount"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task MarkRead_UnknownNotification_ReturnsNotFound()
    {
        await using var factory = new BackendTestFactory();
        var (client, csrfToken) = await AuthenticatedCsrfClientAsync(factory, AccountId);
        using var ownedClient = client;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/notifications/{Guid.NewGuid()}/read");
        request.Headers.Add("X-Csrf-Token", csrfToken);
        using var response = await ownedClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MarkRead_OwnNotification_MarksItReadAndReflectsInSubsequentUnreadCount()
    {
        await using var factory = new BackendTestFactory();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        var notification = new CloudNotificationSnapshot(
            Guid.NewGuid(), CloudNotificationKind.OwnershipReceived, "/dashboard", 1, false, DateTime.UtcNow, DateTime.UtcNow);
        factory.NotificationGateway.Seed(ownerId, notification);

        var (client, csrfToken) = await AuthenticatedCsrfClientAsync(factory, AccountId);
        using var ownedClient = client;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/notifications/{notification.Id}/read");
        request.Headers.Add("X-Csrf-Token", csrfToken);
        using var response = await ownedClient.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        using var unreadResponse = await ownedClient.GetAsync("/notifications/unread-count");
        var body = await unreadResponse.Content.ReadFromJsonAsync<JsonNode>();
        Assert.AreEqual(0, body!["unreadCount"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task MarkRead_AnotherOwnersNotification_ReturnsNotFoundNotForbidden()
    {
        // Issue #34 Red "revoked permissions": a viewer no longer authorized for an owner must get
        // the same generic response as a nonexistent notification, never a distinguishing 403.
        await using var factory = new BackendTestFactory();
        var notification = new CloudNotificationSnapshot(
            Guid.NewGuid(), CloudNotificationKind.OwnershipReceived, "/dashboard", 1, false, DateTime.UtcNow, DateTime.UtcNow);
        factory.NotificationGateway.Seed(Guid.NewGuid(), notification);

        var (client, csrfToken) = await AuthenticatedCsrfClientAsync(factory, AccountId);
        using var ownedClient = client;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/notifications/{notification.Id}/read");
        request.Headers.Add("X-Csrf-Token", csrfToken);
        using var response = await ownedClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
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

    private static async Task<(HttpClient Client, string CsrfToken)> AuthenticatedCsrfClientAsync(BackendTestFactory factory, uint accountId)
    {
        var secret = CloudWebSessionSecretHasher.Generate();
        var token = CloudCsrfTokenGenerator.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, accountId, Guid.NewGuid(), secret.Hash, token, DateTime.UtcNow, TimeSpan.FromHours(1));

        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        return (client, token);
    }
}
