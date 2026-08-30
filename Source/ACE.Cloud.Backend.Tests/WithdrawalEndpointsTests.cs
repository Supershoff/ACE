using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// Issue #33's Red -> Green endpoint coverage for web-initiated Withdrawal Reservation open/cancel
/// (WDR-001, WDR-002, WDR-003, WDR-006, WDR-008).
/// </summary>
[TestClass]
public sealed class WithdrawalEndpointsTests
{
    private const uint AccountId = 42;
    private const string ShardId = "us1";

    [TestMethod]
    public async Task Open_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);

        using var response = await client.PostAsJsonAsync(
            "/withdrawal/reservations", new OpenWithdrawalReservationRequest([new WithdrawalReservationTargetRequest("Item", 1, null, null, null)]));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Open_WithoutCsrfToken_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, AccountId, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), DateTime.UtcNow, TimeSpan.FromHours(1));
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);

        using var response = await client.PostAsJsonAsync(
            "/withdrawal/reservations", new OpenWithdrawalReservationRequest([new WithdrawalReservationTargetRequest("Item", 1, null, null, null)]));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Open_LinkedAccountSession_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AccountOwnershipResolver.SetLinked(AccountId, mainAccountId: 99);
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync(
            "/withdrawal/reservations", new OpenWithdrawalReservationRequest([new WithdrawalReservationTargetRequest("Item", 1, null, null, null)]));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Open_EmptyTargets_ReturnsBadRequest()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync("/withdrawal/reservations", new OpenWithdrawalReservationRequest([]));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Open_WorldBoundaryDown_ReturnsServiceUnavailableAndNeverOpensAReservation()
    {
        // WDR-008: "if ACE is down, block token creation."
        await using var factory = new BackendTestFactory();
        factory.WorldBoundaryHealthProbe.IsHealthy = false;
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync(
            "/withdrawal/reservations", new OpenWithdrawalReservationRequest([new WithdrawalReservationTargetRequest("Item", 1, null, null, null)]));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.HasCount(0, factory.WithdrawalReservationGateway.ReserveCalls);
    }

    [TestMethod]
    public async Task Open_WholeItemTarget_ReturnsTheTokenSecretExactlyOnce()
    {
        await using var factory = new BackendTestFactory();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync(
            "/withdrawal/reservations", new OpenWithdrawalReservationRequest([new WithdrawalReservationTargetRequest("Item", 1234, null, null, null)]));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsFalse(string.IsNullOrWhiteSpace(body!["tokenSecret"]!.GetValue<string>()));
        Assert.IsNotNull(body["reservationId"]);
        Assert.HasCount(1, factory.WithdrawalReservationGateway.ReserveCalls);
    }

    [TestMethod]
    public async Task Open_StackLotFullQuantity_NeverSplitsTheLot()
    {
        await using var factory = new BackendTestFactory();
        var lotId = Guid.NewGuid();
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync(
            "/withdrawal/reservations", new OpenWithdrawalReservationRequest([new WithdrawalReservationTargetRequest("StackLot", null, lotId, null, null)]));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(1, factory.WithdrawalReservationGateway.ReserveCalls);
        var target = factory.WithdrawalReservationGateway.ReserveCalls[0].Single();
        Assert.AreEqual(CloudWithdrawalReservationTargetKind.StackLot, target.Kind);
        Assert.AreEqual(lotId, target.StackLotId);
    }

    [TestMethod]
    public async Task Open_StackLotPartialQuantity_SplitsOffExactlyThatQuantityAndReservesTheNewLot()
    {
        // INV-002: "partial lots."
        await using var factory = new BackendTestFactory();
        var lotId = Guid.NewGuid();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        factory.StackLotSplitGateway.Seed(lotId, new CloudStackLotSnapshot(ownerId, Quantity: 10, Version: 3));
        var newLot = new CloudStackLot(Guid.NewGuid(), ShardId, ownerId, quantity: 4);
        var remainingLot = new CloudStackLot(Guid.NewGuid(), ShardId, ownerId, quantity: 6);
        factory.StackLotSplitGateway.NextSplitOutcome = CloudBoundaryOutcome<CloudStackLotSplitResult>.Committed(
            new CloudStackLotSplitResult(remainingLot, newLot));
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync(
            "/withdrawal/reservations",
            new OpenWithdrawalReservationRequest([new WithdrawalReservationTargetRequest("StackLot", null, lotId, Quantity: 4, ExpectedVersion: 3)]));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var target = factory.WithdrawalReservationGateway.ReserveCalls.Single().Single();
        Assert.AreEqual(newLot.Id, target.StackLotId);
    }

    [TestMethod]
    public async Task Open_StackLotPartialQuantity_NotOwnedByCaller_ReturnsNotFoundAndNeverSplits()
    {
        await using var factory = new BackendTestFactory();
        var lotId = Guid.NewGuid();
        factory.StackLotSplitGateway.Seed(lotId, new CloudStackLotSnapshot(Guid.NewGuid(), Quantity: 10, Version: 3));
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync(
            "/withdrawal/reservations",
            new OpenWithdrawalReservationRequest([new WithdrawalReservationTargetRequest("StackLot", null, lotId, Quantity: 4, ExpectedVersion: 3)]));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.HasCount(0, factory.WithdrawalReservationGateway.ReserveCalls);
    }

    [TestMethod]
    public async Task Open_ReservationGatewayConflict_ReturnsConflictWithReason()
    {
        await using var factory = new BackendTestFactory();
        factory.WithdrawalReservationGateway.NextReserveOutcome = CloudBoundaryOutcome<CloudWithdrawalReservation>.Conflict("already reserved");
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync(
            "/withdrawal/reservations", new OpenWithdrawalReservationRequest([new WithdrawalReservationTargetRequest("Item", 1234, null, null, null)]));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.AreEqual("already reserved", body!["reason"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Cancel_ReservationNotOwnedByCaller_ReturnsNotFound()
    {
        await using var factory = new BackendTestFactory();
        var otherOwnerId = Guid.NewGuid();
        var reservation = CloudWithdrawalReservation.Open(ShardId, otherOwnerId, "hash", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddMinutes(15));
        factory.WithdrawalReservationGateway.Seed(reservation);
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync(
            $"/withdrawal/reservations/{reservation.Id}/cancel", new CancelWithdrawalReservationRequest(1));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_OwnReservation_ReturnsOk()
    {
        await using var factory = new BackendTestFactory();
        var ownerId = CloudOwnerIdentity.ForAccount(ShardId, AccountId);
        var reservation = CloudWithdrawalReservation.Open(ShardId, ownerId, "hash", Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddMinutes(15));
        factory.WithdrawalReservationGateway.Seed(reservation);
        using var client = await AuthenticatedClientAsync(factory, AccountId);

        using var response = await client.PostAsJsonAsync(
            $"/withdrawal/reservations/{reservation.Id}/cancel", new CancelWithdrawalReservationRequest(reservation.Version));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        Assert.IsTrue(body!["cancelled"]!.GetValue<bool>());
    }

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
