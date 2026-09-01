using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.Backend.Tests;

/// <summary>
/// AC Cloud Mule issue #38 Red -> Green endpoint coverage for the audited administrator Allegiance
/// Vault recovery surface (VAULT-005, ADM-002).
/// </summary>
[TestClass]
public sealed class CloudAdminVaultRecoveryEndpointsTests
{
    private const uint AdminAccountId = 5;
    private const uint NonAdminAccountId = 42;
    private const uint DestinationAccountId = 77;
    private const string ShardId = "us1";

    [TestMethod]
    public async Task List_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/admin/vault-recovery");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task List_NonAdminSession_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 1;
        using var client = await AuthenticatedClientAsync(factory, NonAdminAccountId);

        using var response = await client.GetAsync("/admin/vault-recovery");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task List_AdminSession_ReturnsOnlyUnresolvedDiagnostics()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 5;

        var unresolved = SeedDiagnostic(factory, monarchCharacterId: 100);
        var resolved = SeedDiagnostic(factory, monarchCharacterId: 200);
        resolved.Resolve(AdminAccountId, "Already handled.", CloudOwnerIdentity.ForAccount(ShardId, DestinationAccountId));

        using var client = await AuthenticatedClientAsync(factory, AdminAccountId);

        using var response = await client.GetAsync("/admin/vault-recovery");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonNode>();
        var diagnostics = body!["diagnostics"]!.AsArray();
        Assert.HasCount(1, diagnostics);
        Assert.AreEqual(unresolved.Id.ToString(), diagnostics[0]!["id"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Recover_NoSessionCookie_ReturnsUnauthorized()
    {
        await using var factory = new BackendTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);

        using var response = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{Guid.NewGuid()}/recover", new CloudRecoverVaultRequestBody(DestinationAccountId, "reason", true));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Recover_NonAdminSession_ReturnsForbidden_AndNeverMutatesAnything()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 1;
        var diagnostic = SeedDiagnostic(factory, monarchCharacterId: 100);
        using var client = await AuthenticatedClientAsync(factory, NonAdminAccountId);

        using var response = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{diagnostic.Id}/recover", new CloudRecoverVaultRequestBody(DestinationAccountId, "reason", true));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.IsFalse(diagnostic.IsResolved);
    }

    [TestMethod]
    public async Task Recover_WrongOrigin_ReturnsForbidden()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 5;
        var diagnostic = SeedDiagnostic(factory, monarchCharacterId: 100);
        using var client = await AuthenticatedClientAsync(factory, AdminAccountId);
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", "https://not-allowed.example.test");

        using var response = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{diagnostic.Id}/recover", new CloudRecoverVaultRequestBody(DestinationAccountId, "reason", true));

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Recover_WithoutReason_ReturnsConflict_AndDoesNotResolve()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 5;
        var diagnostic = SeedDiagnostic(factory, monarchCharacterId: 100);
        using var client = await AuthenticatedClientAsync(factory, AdminAccountId);

        using var response = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{diagnostic.Id}/recover", new CloudRecoverVaultRequestBody(DestinationAccountId, Reason: null, Confirm: true));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.IsFalse(diagnostic.IsResolved);
    }

    [TestMethod]
    public async Task Recover_WithoutConfirmation_ReturnsConflict_AndDoesNotResolve()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 5;
        var diagnostic = SeedDiagnostic(factory, monarchCharacterId: 100);
        using var client = await AuthenticatedClientAsync(factory, AdminAccountId);

        using var response = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{diagnostic.Id}/recover", new CloudRecoverVaultRequestBody(DestinationAccountId, "A real reason.", Confirm: false));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.IsFalse(diagnostic.IsResolved);
    }

    [TestMethod]
    public async Task Recover_DestinationAccountDoesNotExist_ReturnsConflict_AndDoesNotResolve()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 5;
        // The admin's own session revalidates fine (NextAccessLevel), but the destination account the
        // admin typed does not exist in ace_auth.account -- an override targeted at that account ID
        // specifically, since GetFreshAccessLevelAsync is also used to revalidate the caller.
        factory.AuthBridgeClient.AccessLevelByAccountId[DestinationAccountId] = null;
        var diagnostic = SeedDiagnostic(factory, monarchCharacterId: 100);
        using var client = await AuthenticatedClientAsync(factory, AdminAccountId);

        using var response = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{diagnostic.Id}/recover",
            new CloudRecoverVaultRequestBody(DestinationAccountId, "A real reason.", true));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.IsFalse(diagnostic.IsResolved, "A destination account that does not exist must never be committed -- a resolved diagnostic can never be re-applied.");
    }

    [TestMethod]
    public async Task Recover_UnknownDiagnosticId_ReturnsConflict()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 5;
        using var client = await AuthenticatedClientAsync(factory, AdminAccountId);

        using var response = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{Guid.NewGuid()}/recover", new CloudRecoverVaultRequestBody(DestinationAccountId, "A real reason.", true));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task Recover_ValidRequest_ResolvesTheDiagnosticWithTheAdminsReasonAndChosenDestination()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 5;
        var diagnostic = SeedDiagnostic(factory, monarchCharacterId: 100);
        using var client = await AuthenticatedClientAsync(factory, AdminAccountId);

        using var response = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{diagnostic.Id}/recover",
            new CloudRecoverVaultRequestBody(DestinationAccountId, "Monarch deleted directly in the database; reassigning to the designated successor.", true));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(diagnostic.IsResolved);
        Assert.AreEqual(AdminAccountId, diagnostic.ResolvedByAdminAccountId);
        Assert.AreEqual(CloudOwnerIdentity.ForAccount(ShardId, DestinationAccountId), diagnostic.DestinationOwnerId);
    }

    [TestMethod]
    public async Task Recover_ACommittedTransfer_CanNeverBeOverriddenByASecondAttempt()
    {
        await using var factory = new BackendTestFactory();
        factory.AuthBridgeClient.NextAccessLevel = 5;
        var diagnostic = SeedDiagnostic(factory, monarchCharacterId: 100);
        using var client = await AuthenticatedClientAsync(factory, AdminAccountId);

        var firstReason = "First administrator decision: send to the designated successor account.";
        using var firstResponse = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{diagnostic.Id}/recover", new CloudRecoverVaultRequestBody(DestinationAccountId, firstReason, true));
        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);

        // A retried/duplicate request -- even with a different administrator-chosen destination --
        // must never re-apply or override the already-committed decision.
        using var secondResponse = await client.PostAsJsonAsync(
            $"/admin/vault-recovery/{diagnostic.Id}/recover", new CloudRecoverVaultRequestBody(DestinationAccountId + 1, "A different, later reason.", true));

        Assert.AreEqual(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.AreEqual(CloudOwnerIdentity.ForAccount(ShardId, DestinationAccountId), diagnostic.DestinationOwnerId);
        Assert.AreEqual(firstReason, diagnostic.ResolutionReason);
    }

    private static CloudMonarchDeletionDiagnostic SeedDiagnostic(BackendTestFactory factory, uint monarchCharacterId)
    {
        var diagnostic = new CloudMonarchDeletionDiagnostic(
            ShardId, monarchCharacterId, CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchCharacterId),
            $"Monarch character {monarchCharacterId} no longer exists in ace_shard, but its Allegiance Vault still has contents.");
        factory.MonarchVaultRecoveryService.Diagnostics.Add(diagnostic);
        return diagnostic;
    }

    private static async Task<HttpClient> AuthenticatedClientAsync(BackendTestFactory factory, uint accountId)
    {
        var secret = CloudWebSessionSecretHasher.Generate();
        await factory.SessionStore.ExchangeGrantForSessionAsync(
            ShardId, accountId, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), DateTime.UtcNow, TimeSpan.FromHours(1));

        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", $"ace_cloud_session={secret.Secret}");
        client.DefaultRequestHeaders.Add("Origin", BackendTestFactory.AllowedOrigin);

        var session = await factory.SessionStore.TryGetActiveSessionAsync(secret.Hash, DateTime.UtcNow);
        client.DefaultRequestHeaders.Add(AuthSessionEndpoints.CsrfHeaderName, session!.CsrfToken);

        return client;
    }
}
