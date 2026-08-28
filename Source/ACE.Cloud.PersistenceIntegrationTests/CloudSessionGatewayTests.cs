using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #19's Green bullet "Exchange grants in the backend for secure
/// HttpOnly SameSite sessions" (AUTH-002) and its Red section: "...replayed/expired grants... session
/// rotation, logout/revocation..." Proves the actual MariaDB unique constraint on
/// <see cref="CloudAuthGrantConsumption.Nonce"/> is what rejects a replayed grant, not merely an
/// application-level check.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudSessionGatewayTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await CloudBoundaryTestFixtureData.ResetAsync(_fixture.CloudConnectionString, ShardId);
    }

    [TestMethod]
    public async Task ExchangeGrantForSession_FirstTime_CreatesAnActiveSession()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gateway = new CloudSessionGateway(context);

        var now = DateTime.UtcNow;
        var secret = CloudWebSessionSecretHasher.Generate();

        var result = await gateway.ExchangeGrantForSessionAsync(
            ShardId, accountId: 7, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromHours(1));

        Assert.IsTrue(result.IsCreated);
        Assert.AreEqual(7u, result.Session!.AccountId);
        Assert.IsTrue(result.Session.IsActiveAt(now));
    }

    [TestMethod]
    public async Task ExchangeGrantForSession_ReplayedNonce_IsRejectedByTheDatabasesUniqueConstraint()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var nonce = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using (var firstContext = new CloudDbContext(options))
        {
            var firstGateway = new CloudSessionGateway(firstContext);
            var firstResult = await firstGateway.ExchangeGrantForSessionAsync(
                ShardId, accountId: 7, nonce, CloudWebSessionSecretHasher.Generate().Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromHours(1));
            Assert.IsTrue(firstResult.IsCreated);
        }

        await using var secondContext = new CloudDbContext(options);
        var secondGateway = new CloudSessionGateway(secondContext);
        var replayResult = await secondGateway.ExchangeGrantForSessionAsync(
            ShardId, accountId: 7, nonce, CloudWebSessionSecretHasher.Generate().Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromHours(1));

        Assert.IsFalse(replayResult.IsCreated);
        Assert.AreEqual(CloudSessionExchangeOutcomeKind.GrantAlreadyUsed, replayResult.Kind);
    }

    [TestMethod]
    public async Task TryGetActiveSession_ExpiredSession_ReturnsNull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var now = DateTime.UtcNow;
        var secret = CloudWebSessionSecretHasher.Generate();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudSessionGateway(context);
            var created = await gateway.ExchangeGrantForSessionAsync(
                ShardId, accountId: 7, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromMinutes(1));
            Assert.IsTrue(created.IsCreated);
        }

        await using var lookupContext = new CloudDbContext(options);
        var lookupGateway = new CloudSessionGateway(lookupContext);
        var afterExpiry = await lookupGateway.TryGetActiveSessionAsync(secret.Hash, now.AddMinutes(2));

        Assert.IsNull(afterExpiry);
    }

    [TestMethod]
    public async Task RevokeSession_ThenLookup_NoLongerActive()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var now = DateTime.UtcNow;
        var secret = CloudWebSessionSecretHasher.Generate();

        await using (var context = new CloudDbContext(options))
        {
            var gateway = new CloudSessionGateway(context);
            var created = await gateway.ExchangeGrantForSessionAsync(
                ShardId, accountId: 7, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromHours(1));
            Assert.IsTrue(created.IsCreated);
        }

        await using (var revokeContext = new CloudDbContext(options))
        {
            await new CloudSessionGateway(revokeContext).RevokeSessionAsync(secret.Hash, now.AddMinutes(1));
        }

        await using var lookupContext = new CloudDbContext(options);
        var afterRevoke = await new CloudSessionGateway(lookupContext).TryGetActiveSessionAsync(secret.Hash, now.AddMinutes(2));

        Assert.IsNull(afterRevoke);
    }

    [TestMethod]
    public async Task RevokeSession_AlreadyRevoked_IsIdempotent()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var now = DateTime.UtcNow;
        var secret = CloudWebSessionSecretHasher.Generate();

        await using (var context = new CloudDbContext(options))
        {
            await new CloudSessionGateway(context).ExchangeGrantForSessionAsync(
                ShardId, accountId: 7, Guid.NewGuid(), secret.Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromHours(1));
        }

        await using (var firstRevokeContext = new CloudDbContext(options))
        {
            await new CloudSessionGateway(firstRevokeContext).RevokeSessionAsync(secret.Hash, now.AddMinutes(1));
        }

        // Must not throw revoking an already-revoked session a second time.
        await using var secondRevokeContext = new CloudDbContext(options);
        await new CloudSessionGateway(secondRevokeContext).RevokeSessionAsync(secret.Hash, now.AddMinutes(2));
    }

    [TestMethod]
    public async Task RotateSession_EndsTheOldSessionAndOpensAReplacementForTheSameAccount()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var now = DateTime.UtcNow;
        var originalSecret = CloudWebSessionSecretHasher.Generate();

        Guid originalSessionId;
        await using (var context = new CloudDbContext(options))
        {
            var created = await new CloudSessionGateway(context).ExchangeGrantForSessionAsync(
                ShardId, accountId: 9, Guid.NewGuid(), originalSecret.Hash, CloudCsrfTokenGenerator.Generate(), now, TimeSpan.FromHours(1));
            originalSessionId = created.Session!.Id;
        }

        var newSecret = CloudWebSessionSecretHasher.Generate();
        await using var rotateContext = new CloudDbContext(options);
        var rotated = await new CloudSessionGateway(rotateContext).RotateSessionAsync(
            originalSecret.Hash, newSecret.Hash, CloudCsrfTokenGenerator.Generate(), now.AddMinutes(1), TimeSpan.FromHours(1));

        Assert.IsNotNull(rotated);
        Assert.AreEqual(9u, rotated!.AccountId);
        Assert.AreEqual(originalSessionId, rotated.RotatedFromSessionId);

        await using var lookupContext = new CloudDbContext(options);
        var gateway = new CloudSessionGateway(lookupContext);

        Assert.IsNull(await gateway.TryGetActiveSessionAsync(originalSecret.Hash, now.AddMinutes(2)), "Rotation must end the old session.");
        Assert.IsNotNull(await gateway.TryGetActiveSessionAsync(newSecret.Hash, now.AddMinutes(2)), "Rotation must open an active replacement.");
    }

    [TestMethod]
    public async Task RotateSession_NoActiveSessionForTheGivenSecret_ReturnsNull()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);

        var rotated = await new CloudSessionGateway(context).RotateSessionAsync(
            "unknown-secret-hash", CloudWebSessionSecretHasher.Generate().Hash, CloudCsrfTokenGenerator.Generate(), DateTime.UtcNow, TimeSpan.FromHours(1));

        Assert.IsNull(rotated);
    }
}
