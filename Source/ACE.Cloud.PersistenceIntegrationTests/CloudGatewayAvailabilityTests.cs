using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #11's Red section: "Test... MariaDB unavailable... and
/// incompatible protocol/schema versions." Proves the gateway returns an explicit, typed result
/// instead of an unhandled exception in both cases (transaction rule 8), and that
/// <see cref="CloudGatewayDiagnostics"/> reports the same facts without attempting a mutation.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudGatewayAvailabilityTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 900_000;

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
    public async Task DepositAsync_AgainstAnUnreachableDatabase_ReturnsUnavailable_InsteadOfThrowing()
    {
        var options = CloudDbContextOptionsFactory.Create(UnreachableConnectionString(), await RealServerVersionAsync());
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositAsync(NextId(), ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Unavailable, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "unavailable");
    }

    [TestMethod]
    public async Task CheckDatabaseAvailabilityAsync_AgainstAnUnreachableDatabase_ReturnsUnavailable_WithoutThrowing()
    {
        var options = CloudDbContextOptionsFactory.Create(UnreachableConnectionString(), await RealServerVersionAsync());
        await using var context = new CloudDbContext(options);
        var diagnostics = new CloudGatewayDiagnostics(context);

        var result = await diagnostics.CheckDatabaseAvailabilityAsync();

        Assert.IsFalse(result.IsAvailable);
        Assert.IsNotNull(result.Reason);
    }

    [TestMethod]
    public async Task CheckDatabaseAvailabilityAsync_AgainstTheRealDatabase_ReturnsAvailable()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var diagnostics = new CloudGatewayDiagnostics(context);

        var result = await diagnostics.CheckDatabaseAvailabilityAsync();

        Assert.IsTrue(result.IsAvailable);
    }

    [TestMethod]
    public async Task DepositAsync_WithAnIncompatibleExpectedAceExtensionVersion_RefusesTheMutation_WithoutCommittingAnything()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var incompatible = new CloudComponentVersions("9.9.9-does-not-match", CloudDatabaseFixture.ContractProtocolVersion, CloudDatabaseFixture.ContractProtocolVersion);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context, incompatible);

        var outcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Unavailable, outcome.Kind);
        StringAssert.Contains(outcome.Reason, "version mismatch");

        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));
        await using var verifyContext = new CloudDbContext(options);
        Assert.AreEqual(0, await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId));
    }

    [TestMethod]
    public async Task DepositAsync_WithMatchingExpectedVersions_CommitsNormally()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var matching = new CloudComponentVersions(
            CloudDatabaseFixture.AceExtensionVersion, CloudDatabaseFixture.ContractProtocolVersion, CloudDatabaseFixture.ContractProtocolVersion);

        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context, matching);

        var outcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind);
    }

    [TestMethod]
    public async Task CheckProtocolCompatibilityAsync_ReportsTheSameIncompatibilityAMutationWouldRefuse()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var incompatible = new CloudComponentVersions("9.9.9-does-not-match", CloudDatabaseFixture.ContractProtocolVersion, CloudDatabaseFixture.ContractProtocolVersion);

        await using var context = new CloudDbContext(options);
        var diagnostics = new CloudGatewayDiagnostics(context);

        var result = await diagnostics.CheckProtocolCompatibilityAsync(incompatible);

        Assert.IsFalse(result.IsCompatible);
        Assert.AreEqual(CloudVersionComponent.AceExtension, result.IncompatibleComponent);
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    private string UnreachableConnectionString()
    {
        var builder = new MySqlConnectionStringBuilder(_fixture.CloudConnectionString)
        {
            Server = "127.0.0.1",
            Port = 1,
            ConnectionTimeout = 2,
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// ServerVersion.AutoDetect connects immediately, so an unreachable connection string can never
    /// be used to build options through it -- the connection failure would happen at options-build
    /// time instead of inside the boundary/diagnostics code this test actually exercises. Detecting
    /// against the real, reachable fixture connection and reusing that version for the deliberately
    /// unreachable one sidesteps that without hardcoding a MariaDB version number.
    /// </summary>
    private async Task<ServerVersion> RealServerVersionAsync() =>
        await Task.Run(() => ServerVersion.AutoDetect(_fixture.CloudConnectionString));
}
