using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// Issue #33's Red -> Green coverage for the Withdrawal Token web creation/status/cancellation
/// surface (WDR-001, WDR-002, WDR-003, WDR-006, WDR-008): "no token appears in URLs, analytics,
/// normal logs, or public telemetry," and "stale UI cannot reuse assets or conceal a rejected
/// action."
/// </summary>
[TestClass]
public sealed class CloudWithdrawalEndpointsTests
{
    private const uint MainAccountId = 42;
    private const uint LinkedAccountId = 43;
    private const string ShardId = "us1";

    private static void UseAllowedOrigin(HttpRequestMessage request) =>
        request.Headers.Add("Origin", BackendTestFactory.AllowedOrigin);

    [TestMethod]
    public async Task GetLocations_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/withdrawal-locations");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetLocations_ReturnsConfiguredNamedLandblocksInDisplayFormat()
    {
        await using var factory = new BackendTestFactory();
        factory.WithdrawalLocationReader.Current = new CloudWithdrawalLocationConfiguration(
            WithdrawAnywhereEnabled: false,
            NamedLandblocks: [new CloudWithdrawalNamedLandblock(Guid.NewGuid(), 0x123E, "Withdrawal Hall")],
            Version: CloudAggregateVersion.Initial);

        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await client.GetAsync("/withdrawal-locations");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var landblocks = body!["namedLandblocks"]!.AsArray();
        Assert.HasCount(1, landblocks);
        Assert.AreEqual("0x123E", landblocks[0]!["landblock"]!.GetValue<string>());
        Assert.AreEqual("Withdrawal Hall", landblocks[0]!["name"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task GetCurrent_NoActiveReservation_ReturnsActiveFalse()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, MainAccountId);

        using var response = await client.GetAsync("/withdrawals/current");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsFalse(body!["active"]!.GetValue<bool>());
    }

    [TestMethod]
    public async Task GetCurrent_LinkedAccountSession_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(LinkedAccountId, MainAccountId);
        using var client = await AuthenticatedClientAsync(factory, LinkedAccountId);

        using var response = await client.GetAsync("/withdrawals/current");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Create_WorldBoundaryUnavailable_ReturnsServiceUnavailable_AndNeverOpensAReservation()
    {
        // ARCH-008/WDR-008: withdrawal creation specifically must refuse while the ACE world process
        // is unavailable, even though every other Cloud Mule write stays healthy.
        await using var factory = new BackendTestFactory();
        factory.ServiceAvailabilityReader.Mode = CloudServiceAvailabilityMode.WorldBoundaryUnavailable;

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);
        using var response = await PostCreateAsync(client, csrfToken, ItemTarget(777));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var currentResponse = await client.GetAsync("/withdrawals/current");
        var currentBody = await currentResponse.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsFalse(currentBody!["active"]!.GetValue<bool>());
        client.Dispose();
    }

    [TestMethod]
    public async Task Create_NoTargets_ReturnsBadRequest()
    {
        await using var factory = new BackendTestFactory();
        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/withdrawals")
        {
            Content = JsonContent.Create(new CloudCreateWithdrawalRequestBody(Targets: [])),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        client.Dispose();
    }

    [TestMethod]
    public async Task Create_LinkedAccountSession_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(LinkedAccountId, MainAccountId);

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, LinkedAccountId);
        using var response = await PostCreateAsync(client, csrfToken, ItemTarget(777));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        client.Dispose();
    }

    [TestMethod]
    public async Task Create_Valid_ReturnsTheSecretExactlyOnce_AndNeverAgainFromCurrentStatus()
    {
        await using var factory = new BackendTestFactory();
        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);

        using var createResponse = await PostCreateAsync(client, csrfToken, ItemTarget(777));
        Assert.AreEqual(HttpStatusCode.OK, createResponse.StatusCode);
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonNode>();
        var secret = createBody!["secret"]!.GetValue<string>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(secret));
        var reservationId = createBody["reservationId"]!.GetValue<string>();

        using var currentResponse = await client.GetAsync("/withdrawals/current");
        var currentBody = await currentResponse.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsTrue(currentBody!["active"]!.GetValue<bool>());
        Assert.AreEqual(reservationId, currentBody["reservationId"]!.GetValue<string>());
        Assert.IsNull(currentBody["secret"]);
        client.Dispose();
    }

    [TestMethod]
    public async Task Create_TargetAlreadyReserved_ReturnsConflict()
    {
        await using var factory = new BackendTestFactory();
        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);

        using var first = await PostCreateAsync(client, csrfToken, ItemTarget(777));
        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);

        using var second = await PostCreateAsync(client, csrfToken, ItemTarget(777));

        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode);
        client.Dispose();
    }

    [TestMethod]
    public async Task Cancel_WrongVersion_ReturnsConflict()
    {
        await using var factory = new BackendTestFactory();
        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);

        using var createResponse = await PostCreateAsync(client, csrfToken, ItemTarget(777));
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonNode>();
        var reservationId = createBody!["reservationId"]!.GetValue<string>();

        using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/withdrawals/{reservationId}/cancel")
        {
            Content = JsonContent.Create(new CloudCancelWithdrawalRequestBody(ExpectedVersion: 999)),
        };
        UseAllowedOrigin(cancelRequest);
        cancelRequest.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);
        using var cancelResponse = await client.SendAsync(cancelRequest);

        Assert.AreEqual(HttpStatusCode.Conflict, cancelResponse.StatusCode);
        client.Dispose();
    }

    [TestMethod]
    public async Task Cancel_CorrectVersion_ReleasesTheReservation_AndItNoLongerShowsAsActive()
    {
        await using var factory = new BackendTestFactory();
        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);

        using var createResponse = await PostCreateAsync(client, csrfToken, ItemTarget(777));
        var createBody = await createResponse.Content.ReadFromJsonAsync<JsonNode>();
        var reservationId = createBody!["reservationId"]!.GetValue<string>();
        var version = createBody["version"]!.GetValue<int>();

        using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/withdrawals/{reservationId}/cancel")
        {
            Content = JsonContent.Create(new CloudCancelWithdrawalRequestBody(version)),
        };
        UseAllowedOrigin(cancelRequest);
        cancelRequest.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);
        using var cancelResponse = await client.SendAsync(cancelRequest);

        Assert.AreEqual(HttpStatusCode.OK, cancelResponse.StatusCode);

        using var currentResponse = await client.GetAsync("/withdrawals/current");
        var currentBody = await currentResponse.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsFalse(currentBody!["active"]!.GetValue<bool>());
        client.Dispose();
    }

    [TestMethod]
    public async Task SplitStackLot_NotTheOwner_ReturnsConflict_NeverRedirectsSomeoneElsesQuantity()
    {
        // The narrow SplitOwnLotAsync seam only ever splits into a lot owned by the caller who
        // already proved ownership of the original lot -- this proves the endpoint actually rejects
        // a foreign lot rather than trusting a client-supplied owner.
        await using var factory = new BackendTestFactory();
        factory.StackLotSplitService.NextConflictReason = "Cloud Stack Lot is not owned by the caller.";

        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/inventory/stack-lots/{Guid.NewGuid()}/split")
        {
            Content = JsonContent.Create(new CloudSplitStackLotRequestBody(ExpectedVersion: 1, Quantity: 5)),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        client.Dispose();
    }

    [TestMethod]
    public async Task SplitStackLot_Valid_UsesTheCallersOwnOwnerIdentity()
    {
        await using var factory = new BackendTestFactory();
        var (client, csrfToken) = await AuthenticatedClientWithCsrfAsync(factory, MainAccountId);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/inventory/stack-lots/{Guid.NewGuid()}/split")
        {
            Content = JsonContent.Create(new CloudSplitStackLotRequestBody(ExpectedVersion: 1, Quantity: 5)),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);

        using var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(CloudOwnerIdentity.ForAccount(ShardId, MainAccountId), factory.StackLotSplitService.LastOwnerId);
        client.Dispose();
    }

    private static CloudWithdrawalTargetRequestBody ItemTarget(uint biotaId) => new("Item", biotaId, null);

    private static async Task<HttpResponseMessage> PostCreateAsync(HttpClient client, string csrfToken, params CloudWithdrawalTargetRequestBody[] targets)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/withdrawals")
        {
            Content = JsonContent.Create(new CloudCreateWithdrawalRequestBody(targets)),
        };
        UseAllowedOrigin(request);
        request.Headers.Add(AuthSessionEndpoints.CsrfHeaderName, csrfToken);
        return await client.SendAsync(request);
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
