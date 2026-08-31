using ACE.Cloud.Persistence;
using ACE.Cloud.Persistence.Migrations;
using MySqlConnector;

// Test tooling only (issue #34's disposable local acceptance launcher). Two modes, dispatched by
// args[0] (default "migrate-and-bootstrap" for backward compatibility with existing invocations):
//
//   migrate-and-bootstrap  Applies the Cloud schema's existing migrations (CloudSchemaMigrator,
//                          already covered by ACE.Cloud.PersistenceIntegrationTests) against the
//                          throwaway MariaDB container Prepare-LocalAcceptanceCloudDatabase.ps1 just
//                          started, then idempotently bootstraps (or strictly validates) the
//                          mandatory singleton CloudShardBinding row (blocking defect #2: migrations
//                          alone left every companion startup check permanently reporting "Operator
//                          Bootstrap has not completed").
//
//   validate-external-connection <label> <connectionString>
//                          A read-only "SELECT 1" reachability probe against an operator-owned
//                          database (ace_auth/ace_shard/ace_world) this launcher must never create,
//                          migrate, or purge (blocking defect #4). Never mutates anything.
//
// This intentionally does not create schemas, identities, or secrets for a real deployment -- that is
// the Operator Bootstrap command's production job (CONTEXT.md), out of scope here. The disposable
// container's own `ace_cloud` database and user are created by MariaDB's standard image
// initialization (Tools/LocalAcceptance/docker-compose.acceptance.yml), not by this tool.
var mode = args.Length > 0 ? args[0] : "migrate-and-bootstrap";

return mode switch
{
    "migrate-and-bootstrap" => await MigrateAndBootstrapAsync(),
    "validate-external-connection" => await ValidateExternalConnectionAsync(args),
    _ => Unknown(mode),
};

static int Unknown(string mode)
{
    Console.Error.WriteLine($"Unknown mode '{mode}'. Expected 'migrate-and-bootstrap' or 'validate-external-connection'.");
    return 1;
}

static async Task<int> MigrateAndBootstrapAsync()
{
    var connectionString = Environment.GetEnvironmentVariable("ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.Error.WriteLine(
            "ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING is not set. Prepare-LocalAcceptanceCloudDatabase.ps1 sets " +
            "this from acceptance.settings.json before running this tool; do not run it standalone against an " +
            "unknown database.");
        return 1;
    }

    Console.WriteLine("Applying Cloud schema migrations to the disposable local acceptance database...");
    await CloudSchemaMigrator.MigrateAsync(connectionString);
    Console.WriteLine("Cloud schema migrations applied.");

    var shardId = Environment.GetEnvironmentVariable("ACE_CLOUD_ACCEPTANCE_SHARD_ID");
    if (string.IsNullOrWhiteSpace(shardId))
    {
        Console.Error.WriteLine(
            "ACE_CLOUD_ACCEPTANCE_SHARD_ID is not set. Prepare-LocalAcceptanceCloudDatabase.ps1 sets this from " +
            "acceptance.settings.json's shardId before running this tool.");
        return 1;
    }

    var aceExtensionVersion = Environment.GetEnvironmentVariable("ACE_CLOUD_ACCEPTANCE_ACE_EXTENSION_VERSION") ?? "0.1.0";
    var contractProtocolVersion = Environment.GetEnvironmentVariable("ACE_CLOUD_ACCEPTANCE_CONTRACT_PROTOCOL_VERSION") ?? "0.1.0";

    Console.WriteLine(
        $"Bootstrapping CloudShardBinding (ShardId={shardId}, SchemaVersion={CloudSchemaInfo.CurrentVersion}, " +
        $"AceExtensionVersion={aceExtensionVersion}, ContractProtocolVersion={contractProtocolVersion})...");

    try
    {
        var result = await CloudShardBindingBootstrapper.BootstrapAsync(
            connectionString, shardId, CloudSchemaInfo.CurrentVersion, aceExtensionVersion, contractProtocolVersion);

        Console.WriteLine(result.WasCreated
            ? "CloudShardBinding created."
            : "CloudShardBinding already matches (idempotent no-op).");
    }
    catch (CloudShardBindingMismatchException ex)
    {
        Console.Error.WriteLine($"Aborting: {ex.Message}");
        return 1;
    }

    return 0;
}

static async Task<int> ValidateExternalConnectionAsync(string[] args)
{
    if (args.Length < 3 || string.IsNullOrWhiteSpace(args[1]) || string.IsNullOrWhiteSpace(args[2]))
    {
        Console.Error.WriteLine("Usage: validate-external-connection <label> <connectionString>");
        return 1;
    }

    var label = args[1];
    var connectionString = args[2];

    try
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        await command.ExecuteScalarAsync();

        Console.WriteLine($"{label}: reachable.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"{label}: NOT reachable -- {ex.Message}");
        return 1;
    }
}
