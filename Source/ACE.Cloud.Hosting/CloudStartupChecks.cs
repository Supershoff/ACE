using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using MySqlConnector;

namespace ACE.Cloud.Hosting;

/// <summary>
/// Standard <see cref="CloudStartupDiagnosticsService"/> check factories shared by every companion
/// host. The Cloud Backend and Worker (which own the <c>ace_cloud</c> schema through
/// <see cref="CloudGatewayDiagnostics"/>) use <see cref="Database"/>, <see cref="ShardIdentity"/>, and
/// <see cref="SchemaAndProtocolCompatibility"/>; the Auth Bridge (which has no Cloud schema access at
/// all -- ARCH-004) uses only <see cref="RawConnectionAvailability"/> against its own restricted
/// <c>ace_auth</c>-read identity. <see cref="WorldBoundary"/> applies to any host that gates a
/// world-boundary operation (ARCH-008).
/// </summary>
public static class CloudStartupChecks
{
    public static Func<CancellationToken, Task<CloudStartupCheckResult>> Database(CloudGatewayDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return async cancellationToken =>
        {
            var result = await diagnostics.CheckDatabaseAvailabilityAsync(cancellationToken).ConfigureAwait(false);
            return result.IsAvailable
                ? CloudStartupCheckResult.Healthy(CloudStartupComponent.Database)
                : CloudStartupCheckResult.Unhealthy(CloudStartupComponent.Database, result.Reason!);
        };
    }

    /// <summary>
    /// A database-availability check for a host with no <see cref="CloudGatewayDiagnostics"/> of its
    /// own (the Auth Bridge has no Cloud schema access), probing an arbitrary connection string
    /// directly instead.
    /// </summary>
    public static Func<CancellationToken, Task<CloudStartupCheckResult>> RawConnectionAvailability(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A connection string is required.", nameof(connectionString));
        }

        return async cancellationToken =>
        {
            try
            {
                await using var connection = new MySqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1;";
                await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

                return CloudStartupCheckResult.Healthy(CloudStartupComponent.Database);
            }
            catch (MySqlException ex)
            {
                return CloudStartupCheckResult.Unhealthy(CloudStartupComponent.Database, $"The database is unavailable: {ex.Message}");
            }
        };
    }

    public static Func<CancellationToken, Task<CloudStartupCheckResult>> ShardIdentity(CloudGatewayDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        return async cancellationToken =>
        {
            var hasBinding = await diagnostics.HasShardBindingAsync(cancellationToken).ConfigureAwait(false);
            return hasBinding
                ? CloudStartupCheckResult.Healthy(CloudStartupComponent.ShardIdentity)
                : CloudStartupCheckResult.Unhealthy(
                    CloudStartupComponent.ShardIdentity,
                    "This deployment has no CloudShardBinding row; Operator Bootstrap has not completed.");
        };
    }

    /// <summary>
    /// Compares this deployment's applied schema/extension/protocol versions against <paramref name="expected"/>
    /// (OPS-002), reporting a Cloud schema mismatch as <see cref="CloudStartupComponent.SchemaMigration"/>
    /// ("migration mismatch") distinctly from an ACE extension or contract protocol mismatch
    /// (<see cref="CloudStartupComponent.ContractProtocol"/>, "incompatible ACE protocol"). Callers
    /// should run <see cref="ShardIdentity"/> first: if no CloudShardBinding row exists,
    /// <see cref="CloudGatewayDiagnostics.CheckProtocolCompatibilityAsync"/> would itself report an
    /// incompatibility, but <see cref="ShardIdentity"/>'s more precise diagnosis is what should
    /// actually surface.
    /// </summary>
    public static Func<CancellationToken, Task<CloudStartupCheckResult>> SchemaAndProtocolCompatibility(
        CloudGatewayDiagnostics diagnostics, CloudComponentVersions expected)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(expected);

        return async cancellationToken =>
        {
            var result = await diagnostics.CheckProtocolCompatibilityAsync(expected, cancellationToken).ConfigureAwait(false);
            if (result.IsCompatible)
            {
                return CloudStartupCheckResult.Healthy(CloudStartupComponent.ContractProtocol);
            }

            var component = result.IncompatibleComponent == CloudVersionComponent.CloudSchema
                ? CloudStartupComponent.SchemaMigration
                : CloudStartupComponent.ContractProtocol;

            return CloudStartupCheckResult.Unhealthy(component, result.Reason!);
        };
    }

    public static Func<CancellationToken, Task<CloudStartupCheckResult>> WorldBoundary(ICloudWorldBoundaryHealthProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        return probe.CheckAsync;
    }
}
