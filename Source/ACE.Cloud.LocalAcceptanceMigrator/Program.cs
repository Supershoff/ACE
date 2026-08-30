using ACE.Cloud.Persistence.Migrations;

// Test tooling only (issue #34's disposable local acceptance launcher): applies the Cloud schema's
// existing migrations (CloudSchemaMigrator, already covered by ACE.Cloud.PersistenceIntegrationTests)
// against the throwaway MariaDB container Tools/LocalAcceptance/Start-LocalAcceptance.ps1 just
// started. This intentionally does not create schemas, identities, or secrets -- that is the
// Operator Bootstrap command's production job (CONTEXT.md), out of scope here. The disposable
// container's own `ace_cloud` database and user are created by MariaDB's standard image
// initialization (Tools/LocalAcceptance/docker-compose.acceptance.yml), not by this tool.
var connectionString = Environment.GetEnvironmentVariable("ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine(
        "ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING is not set. Start-LocalAcceptance.ps1 sets this from " +
        "acceptance.settings.json before running this tool; do not run it standalone against an " +
        "unknown database.");
    return 1;
}

Console.WriteLine("Applying Cloud schema migrations to the disposable local acceptance database...");
await CloudSchemaMigrator.MigrateAsync(connectionString);
Console.WriteLine("Cloud schema migrations applied.");
return 0;
