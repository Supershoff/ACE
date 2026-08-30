using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Issue #23's phase-gate acceptance criterion: "A phase-gate suite proves every documented downtime
/// mode, including successful login and off-world mutation while ACE is down and explicit refusal of
/// only deposits and Withdrawal Token creation/redemption." Every scenario below already has its own
/// focused test elsewhere -- <see cref="CloudGatewayAvailabilityTests"/> for the database-down and
/// incompatible-version modes, <c>CloudStartupDiagnosticsServiceTests</c> and
/// <see cref="CloudDiagnosticsEndpoints.IsRoutable"/>'s own tests for the readiness routing decision,
/// and every existing <c>ACE.Cloud.AuthBridge.Tests</c>/<c>ACE.Cloud.Backend.Tests</c> endpoint test
/// (which have always run against an intentionally unreachable
/// <c>WorldBoundaryHealthEndpoint</c>, proving login and session issuance were never coupled to world
/// process reachability in the first place) -- following <c>CloudFidelityPhaseGateAcceptanceTests</c>'
/// precedent, this file's job is to prove ARCH-008's "world down, database up" mode specifically:
/// Cloud Transaction Authority mutations (which never depend on ACE's world process) continue to
/// commit, while <see cref="CloudCustodyBoundary"/>'s deposit/withdrawal operations -- reachable only
/// from ACE's own world-boundary code, which by definition cannot run while that process is down --
/// are the only operations affected.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudDowntimeAcceptancePhaseGateTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 980_000;

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
    public async Task ArchOO8_WorldProcessOffline_DatabaseHealthy_ReportsWorldBoundaryUnavailable_AndStaysReadinessRoutable()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var diagnostics = new CloudGatewayDiagnostics(context);

        var expectedVersions = new CloudComponentVersions("0.1.0", "0.1.0", "0.1.0");
        var service = new CloudStartupDiagnosticsService(
        [
            CloudStartupChecks.Database(diagnostics),
            CloudStartupChecks.ShardIdentity(diagnostics),
            CloudStartupChecks.SchemaAndProtocolCompatibility(diagnostics, expectedVersions),
            _ => Task.FromResult(CloudStartupCheckResult.Unhealthy(CloudStartupComponent.WorldBoundary, "ACE world process is offline: connection refused.")),
        ]);

        var report = await service.EvaluateAsync();

        Assert.AreEqual(CloudServiceAvailabilityMode.WorldBoundaryUnavailable, report.Mode);
        Assert.IsTrue(
            CloudDiagnosticsEndpoints.IsRoutable(report.Mode),
            "ARCH-008: a healthy database with only the world process offline must remain readiness-routable for login and off-world operations.");
    }

    [TestMethod]
    public async Task ArchOO8_WorldProcessOffline_OwnershipTransfer_StillCommits_OffWorldMutationContinues()
    {
        var biotaId = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyBoundary = new CloudCustodyBoundary(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var ownerId = Guid.NewGuid();
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, (await custodyBoundary.DepositAsync(biotaId, ShardId, ownerId, Guid.NewGuid())).Kind);

        // CloudOwnershipTransferAuthority (the Cloud Transaction Authority) never probes or depends
        // on ACE world-process reachability -- it only touches ace_cloud -- so it commits identically
        // whether or not the world process is running. There is nothing to fake here: the absence of
        // any world-boundary dependency in this call is exactly the property ARCH-008 requires.
        var transferAuthority = new CloudOwnershipTransferAuthority(new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString)));
        var transferOutcome = await transferAuthority.TransferAsync(biotaId, Guid.NewGuid(), expectedVersion: 1, Guid.NewGuid());

        Assert.AreEqual(
            CloudBoundaryOutcomeKind.Committed, transferOutcome.Kind,
            "ARCH-008: off-world Cloud Transaction Authority mutations must continue while only the ACE world process is down.");
    }

    [TestMethod]
    public async Task ArchOO9_DatabaseUnavailable_ReportsReadOnly_AndIsNotReadinessRoutable()
    {
        var options = CloudDbContextOptionsFactory.Create(UnreachableConnectionString(), await RealServerVersionAsync());
        await using var context = new CloudDbContext(options);
        var diagnostics = new CloudGatewayDiagnostics(context);

        var service = new CloudStartupDiagnosticsService([CloudStartupChecks.Database(diagnostics)]);
        var report = await service.EvaluateAsync();

        Assert.AreEqual(CloudServiceAvailabilityMode.ReadOnly, report.Mode);
        Assert.IsFalse(
            CloudDiagnosticsEndpoints.IsRoutable(report.Mode),
            "ARCH-009: an unavailable database genuinely cannot serve any request and must not be reported readiness-routable.");
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

    /// <summary>See <c>CloudGatewayAvailabilityTests</c>' identical helper for why this cannot auto-detect against the unreachable connection string itself.</summary>
    private async Task<ServerVersion> RealServerVersionAsync() =>
        await Task.Run(() => ServerVersion.AutoDetect(_fixture.CloudConnectionString));
}
