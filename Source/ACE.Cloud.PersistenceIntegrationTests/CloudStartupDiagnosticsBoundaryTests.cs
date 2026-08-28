using ACE.Cloud.Domain;
using ACE.Cloud.Hosting;
using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #18's Red section: "Add startup tests for missing shard identity,
/// migration mismatch, incompatible ACE protocol, unavailable database, and world process offline"
/// against a real disposable MariaDB fixture, using the exact <see cref="CloudStartupChecks"/>
/// factories the companion hosts wire into their own <see cref="CloudStartupDiagnosticsService"/>.
/// "World process offline" is deliberately not repeated here: it never touches this database fixture
/// (ACE.Cloud.Hosting.Tests.HttpCloudWorldBoundaryHealthProbeTests already proves it against a fake
/// HTTP handler, matching ARCH-003/ARCH-004's "no live ACE world-object coupling" -- there is no
/// ACE.Server process this project could reach even if it wanted to).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStartupDiagnosticsBoundaryTests
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
    public async Task ShardIdentityCheck_WithNoCloudShardBindingRow_ReportsMissingShardIdentity()
    {
        await DeleteShardBindingRowAsync();

        var result = await RunShardIdentityCheckAsync();

        Assert.IsFalse(result.IsHealthy);
        Assert.AreEqual(CloudStartupComponent.ShardIdentity, result.Component);
        StringAssert.Contains(result.Reason, "Operator Bootstrap has not completed");
    }

    [TestMethod]
    public async Task ShardIdentityCheck_WithACloudShardBindingRow_ReportsHealthy()
    {
        var result = await RunShardIdentityCheckAsync();

        Assert.IsTrue(result.IsHealthy);
    }

    [TestMethod]
    public async Task SchemaAndProtocolCompatibilityCheck_WithAMismatchedSchemaVersion_ReportsSchemaMigrationMismatch()
    {
        await SetShardBindingVersionsAsync(schemaVersion: "0.0.1-superseded");

        var result = await RunSchemaAndProtocolCompatibilityCheckAsync();

        Assert.IsFalse(result.IsHealthy);
        Assert.AreEqual(CloudStartupComponent.SchemaMigration, result.Component);
    }

    [TestMethod]
    public async Task SchemaAndProtocolCompatibilityCheck_WithAMismatchedContractProtocolVersion_ReportsIncompatibleAceProtocol()
    {
        await SetShardBindingVersionsAsync(contractProtocolVersion: "9.9.9-does-not-match");

        var result = await RunSchemaAndProtocolCompatibilityCheckAsync();

        Assert.IsFalse(result.IsHealthy);
        Assert.AreEqual(CloudStartupComponent.ContractProtocol, result.Component);
    }

    [TestMethod]
    public async Task SchemaAndProtocolCompatibilityCheck_WithAMismatchedAceExtensionVersion_ReportsIncompatibleAceProtocol()
    {
        await SetShardBindingVersionsAsync(aceExtensionVersion: "9.9.9-does-not-match");

        var result = await RunSchemaAndProtocolCompatibilityCheckAsync();

        Assert.IsFalse(result.IsHealthy);
        Assert.AreEqual(CloudStartupComponent.ContractProtocol, result.Component);
    }

    [TestMethod]
    public async Task SchemaAndProtocolCompatibilityCheck_WithMatchingVersions_ReportsHealthy()
    {
        var result = await RunSchemaAndProtocolCompatibilityCheckAsync();

        Assert.IsTrue(result.IsHealthy);
    }

    [TestMethod]
    public async Task DatabaseCheck_AgainstAnUnreachableDatabase_ReportsUnavailable_InsteadOfThrowing()
    {
        var options = CloudDbContextOptionsFactory.Create(UnreachableConnectionString(), await RealServerVersionAsync());
        await using var context = new CloudDbContext(options);
        var gatewayDiagnostics = new CloudGatewayDiagnostics(context);

        var result = await CloudStartupChecks.Database(gatewayDiagnostics)(CancellationToken.None);

        Assert.IsFalse(result.IsHealthy);
        Assert.AreEqual(CloudStartupComponent.Database, result.Component);
    }

    [TestMethod]
    public async Task DatabaseCheck_AgainstTheRealDatabase_ReportsHealthy()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gatewayDiagnostics = new CloudGatewayDiagnostics(context);

        var result = await CloudStartupChecks.Database(gatewayDiagnostics)(CancellationToken.None);

        Assert.IsTrue(result.IsHealthy);
    }

    [TestMethod]
    public async Task FullDiagnosticsService_WithNoBindingRow_StopsAtShardIdentity_NeverEvaluatingProtocolCompatibility()
    {
        await DeleteShardBindingRowAsync();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gatewayDiagnostics = new CloudGatewayDiagnostics(context);
        var expected = new CloudComponentVersions(
            CloudDatabaseFixture.AceExtensionVersion, CloudSchemaInfo.CurrentVersion, CloudDatabaseFixture.ContractProtocolVersion);

        var service = new CloudStartupDiagnosticsService(
        [
            CloudStartupChecks.Database(gatewayDiagnostics),
            CloudStartupChecks.ShardIdentity(gatewayDiagnostics),
            CloudStartupChecks.SchemaAndProtocolCompatibility(gatewayDiagnostics, expected),
        ]);

        var report = await service.EvaluateAsync();

        Assert.AreEqual(CloudServiceAvailabilityMode.VersionIncompatible, report.Mode);
        Assert.HasCount(2, report.Results, "Database then ShardIdentity should run; SchemaAndProtocolCompatibility must be skipped.");
        Assert.AreEqual(CloudStartupComponent.ShardIdentity, report.Results[^1].Component);
    }

    private async Task<CloudStartupCheckResult> RunShardIdentityCheckAsync()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gatewayDiagnostics = new CloudGatewayDiagnostics(context);

        return await CloudStartupChecks.ShardIdentity(gatewayDiagnostics)(CancellationToken.None);
    }

    private async Task<CloudStartupCheckResult> RunSchemaAndProtocolCompatibilityCheckAsync()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var gatewayDiagnostics = new CloudGatewayDiagnostics(context);
        var expected = new CloudComponentVersions(
            CloudDatabaseFixture.AceExtensionVersion, CloudSchemaInfo.CurrentVersion, CloudDatabaseFixture.ContractProtocolVersion);

        return await CloudStartupChecks.SchemaAndProtocolCompatibility(gatewayDiagnostics, expected)(CancellationToken.None);
    }

    private async Task DeleteShardBindingRowAsync()
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CloudShardBinding;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetShardBindingVersionsAsync(
        string? schemaVersion = null, string? aceExtensionVersion = null, string? contractProtocolVersion = null)
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CloudShardBinding
            SET SchemaVersion = @schemaVersion, AceExtensionVersion = @aceExtensionVersion, ContractProtocolVersion = @contractProtocolVersion
            WHERE Id = 1;
            """;
        command.Parameters.AddWithValue("@schemaVersion", schemaVersion ?? CloudSchemaInfo.CurrentVersion);
        command.Parameters.AddWithValue("@aceExtensionVersion", aceExtensionVersion ?? CloudDatabaseFixture.AceExtensionVersion);
        command.Parameters.AddWithValue("@contractProtocolVersion", contractProtocolVersion ?? CloudDatabaseFixture.ContractProtocolVersion);
        await command.ExecuteNonQueryAsync();
    }

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
    /// be used to build options through it directly; detecting against the real, reachable fixture
    /// connection and reusing that version for the deliberately unreachable one sidesteps that.
    /// </summary>
    private async Task<ServerVersion> RealServerVersionAsync() =>
        await Task.Run(() => ServerVersion.AutoDetect(_fixture.CloudConnectionString));
}
