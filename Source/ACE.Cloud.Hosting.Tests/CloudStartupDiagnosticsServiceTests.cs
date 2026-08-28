using ACE.Cloud.Hosting;

namespace ACE.Cloud.Hosting.Tests;

/// <summary>
/// Red -> Green tests for issue #18's Red section: "Add startup tests for missing shard identity,
/// migration mismatch, incompatible ACE protocol, unavailable database, and world process offline"
/// and its acceptance criterion "Startup/health diagnostics identify the incompatible or unavailable
/// component precisely." Uses fake check delegates rather than a real database or HTTP endpoint so
/// these tests exercise <see cref="CloudStartupDiagnosticsService"/>'s own ordering/short-circuit and
/// mode-mapping logic in isolation; ACE.Cloud.PersistenceIntegrationTests separately proves the same
/// component identification against a real MariaDB fixture using <see cref="CloudStartupChecks"/>.
/// </summary>
[TestClass]
public sealed class CloudStartupDiagnosticsServiceTests
{
    [TestMethod]
    public async Task EvaluateAsync_WithEveryCheckHealthy_ReturnsOperational()
    {
        var service = new CloudStartupDiagnosticsService(
        [
            Healthy(CloudStartupComponent.Database),
            Healthy(CloudStartupComponent.ShardIdentity),
            Healthy(CloudStartupComponent.ContractProtocol),
            Healthy(CloudStartupComponent.WorldBoundary),
        ]);

        var report = await service.EvaluateAsync();

        Assert.AreEqual(CloudServiceAvailabilityMode.Operational, report.Mode);
        Assert.IsTrue(report.IsFullyOperational);
        Assert.HasCount(4, report.Results);
    }

    [TestMethod]
    public async Task EvaluateAsync_WithAnUnavailableDatabase_ReturnsReadOnly_AndSkipsLaterChecks()
    {
        var laterCheckWasCalled = false;

        var service = new CloudStartupDiagnosticsService(
        [
            Unhealthy(CloudStartupComponent.Database, "The Cloud schema database is unavailable: connection refused."),
            _ =>
            {
                laterCheckWasCalled = true;
                return Task.FromResult(CloudStartupCheckResult.Healthy(CloudStartupComponent.ShardIdentity));
            },
        ]);

        var report = await service.EvaluateAsync();

        Assert.AreEqual(CloudServiceAvailabilityMode.ReadOnly, report.Mode);
        Assert.HasCount(1, report.Results);
        Assert.AreEqual(CloudStartupComponent.Database, report.Results[0].Component);
        Assert.IsFalse(laterCheckWasCalled, "ARCH-009: an unavailable database makes every later check moot; it must short-circuit.");
    }

    [TestMethod]
    public async Task EvaluateAsync_WithNoShardBindingRow_ReturnsVersionIncompatible_IdentifyingShardIdentity()
    {
        var service = new CloudStartupDiagnosticsService(
        [
            Healthy(CloudStartupComponent.Database),
            Unhealthy(CloudStartupComponent.ShardIdentity, "This deployment has no CloudShardBinding row; Operator Bootstrap has not completed."),
        ]);

        var report = await service.EvaluateAsync();

        Assert.AreEqual(CloudServiceAvailabilityMode.VersionIncompatible, report.Mode);
        Assert.AreEqual(CloudStartupComponent.ShardIdentity, report.Results[^1].Component);
    }

    [TestMethod]
    public async Task EvaluateAsync_WithAMismatchedSchemaVersion_ReturnsVersionIncompatible_IdentifyingSchemaMigration()
    {
        var service = new CloudStartupDiagnosticsService(
        [
            Healthy(CloudStartupComponent.Database),
            Healthy(CloudStartupComponent.ShardIdentity),
            Unhealthy(CloudStartupComponent.SchemaMigration, "Cloud schema version mismatch: expected 0.2.0, found 0.1.0."),
        ]);

        var report = await service.EvaluateAsync();

        Assert.AreEqual(CloudServiceAvailabilityMode.VersionIncompatible, report.Mode);
        Assert.AreEqual(CloudStartupComponent.SchemaMigration, report.Results[^1].Component);
    }

    [TestMethod]
    public async Task EvaluateAsync_WithAMismatchedContractProtocolVersion_ReturnsVersionIncompatible_IdentifyingContractProtocol()
    {
        var service = new CloudStartupDiagnosticsService(
        [
            Healthy(CloudStartupComponent.Database),
            Healthy(CloudStartupComponent.ShardIdentity),
            Unhealthy(CloudStartupComponent.ContractProtocol, "Contract protocol version mismatch: expected 0.2.0, found 0.1.0."),
        ]);

        var report = await service.EvaluateAsync();

        Assert.AreEqual(CloudServiceAvailabilityMode.VersionIncompatible, report.Mode);
        Assert.AreEqual(CloudStartupComponent.ContractProtocol, report.Results[^1].Component);
    }

    [TestMethod]
    public async Task EvaluateAsync_WithTheWorldProcessOffline_ReturnsWorldBoundaryUnavailable_NotAGenericFailure()
    {
        var service = new CloudStartupDiagnosticsService(
        [
            Healthy(CloudStartupComponent.Database),
            Healthy(CloudStartupComponent.ShardIdentity),
            Healthy(CloudStartupComponent.ContractProtocol),
            Unhealthy(CloudStartupComponent.WorldBoundary, "ACE world process is offline: connection refused."),
        ]);

        var report = await service.EvaluateAsync();

        Assert.AreEqual(
            CloudServiceAvailabilityMode.WorldBoundaryUnavailable, report.Mode,
            "ARCH-008: the world process being offline must degrade to a boundary-only unavailable mode, not the same ReadOnly mode a database outage produces.");
        Assert.AreEqual(CloudStartupComponent.WorldBoundary, report.Results[^1].Component);
    }

    [TestMethod]
    public void CloudStartupCheckResult_Unhealthy_RequiresANonEmptyReason()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudStartupCheckResult.Unhealthy(CloudStartupComponent.Database, ""));
    }

    private static Func<CancellationToken, Task<CloudStartupCheckResult>> Healthy(CloudStartupComponent component) =>
        _ => Task.FromResult(CloudStartupCheckResult.Healthy(component));

    private static Func<CancellationToken, Task<CloudStartupCheckResult>> Unhealthy(CloudStartupComponent component, string reason) =>
        _ => Task.FromResult(CloudStartupCheckResult.Unhealthy(component, reason));
}
