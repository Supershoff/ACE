using System.Net;

using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>Issue #34 Red -> Green endpoint coverage for the resumable private Live State Stream HTTP surface (EVT-007).</summary>
[TestClass]
public sealed class CloudLiveStreamEndpointsTests
{
    private const uint AccountId = 42;
    private const string ShardId = "us1";

    [TestMethod]
    public async Task Stream_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/live-stream");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Stream_LinkedAccountSession_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(AccountId, mainAccountId: 99);
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.GetAsync("/live-stream");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Stream_Authenticated_OpensAsServerSentEventsAndReportsCurrentAvailabilityMode()
    {
        await using var factory = new BackendTestFactory();
        factory.ServiceAvailabilityReader.Mode = CloudServiceAvailabilityMode.ReadOnly;

        using var client = await AuthenticatedClientAsync(factory, AccountId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync("/live-stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var firstLine = await reader.ReadLineAsync(cts.Token);

        StringAssert.Contains(firstLine, "\"kind\":\"state\"");
        StringAssert.Contains(firstLine, "\"mode\":\"ReadOnly\"");
    }

    [TestMethod]
    public async Task Stream_Authenticated_DeliversASeededPrivateEventScopedToTheViewer()
    {
        await using var factory = new BackendTestFactory();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        factory.LiveStreamReader.Events.Add(new CloudLiveStreamEvent(
            ShardId, sequenceNumber: 1, isPublic: false, ownerId, "Notification", Guid.NewGuid(), sourceSequenceNumber: 1));

        using var client = await AuthenticatedClientAsync(factory, AccountId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync("/live-stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // First line is the leading "state" message (see the test above); the event follows.
        await reader.ReadLineAsync(cts.Token);
        await reader.ReadLineAsync(cts.Token); // blank line separator after the state message
        var idLine = await reader.ReadLineAsync(cts.Token);
        var dataLine = await reader.ReadLineAsync(cts.Token);

        StringAssert.StartsWith(idLine, "id: 1");
        StringAssert.Contains(dataLine, "\"eventKind\":\"Notification\"");
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
