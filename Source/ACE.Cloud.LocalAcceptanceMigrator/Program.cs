using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Cloud.Persistence.Migrations;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

// Test tooling only (issue #34's disposable local acceptance launcher). Modes are dispatched by
// args[0] (default "migrate-and-bootstrap" for backward compatibility with existing invocations):
//
//   migrate-and-bootstrap  Applies the Cloud schema's existing migrations (CloudSchemaMigrator,
//                          already covered by ACE.Cloud.PersistenceIntegrationTests) against the
//                          prepared ace_cloud schema, then idempotently bootstraps (or strictly validates) the
//                          mandatory singleton CloudShardBinding row (blocking defect #2: migrations
//                          alone left every companion startup check permanently reporting "Operator
//                          Bootstrap has not completed").
//
//   validate-external-connection <label> <connectionString>
//                          A read-only "SELECT 1" reachability probe against an operator-owned
//                          database (ace_auth/ace_shard/ace_world) this launcher must never create,
//                          migrate, or purge (blocking defect #4). Never mutates anything.
//
//   prepare-colocated     Creates disposable ace_cloud beside the disposable ace_shard schema,
//                         migrates it with the local admin identity, bootstraps CloudShardBinding,
//                         and grants a separate runtime identity access only to ace_cloud.
//
//   purge-colocated       Drops only the known Cloud Mule triggers from ace_shard and the disposable
//                         ace_cloud schema. Used exclusively by Stop-LocalAcceptance.ps1 -Purge.
//
//   activate-portal-dat <path>
//                         Drives the existing CloudAssetImportBoundary chunked-upload/finalize API
//                         against an operator-supplied local client_portal.dat, waits for the running
//                         ACE.Cloud.Worker process's CloudAssetImportStagingWorker to extract and
//                         stage it, then activates the resulting manifest (issue #34: "least-
//                         resistance local-acceptance/operator path to stage and activate
//                         client_portal.dat"). Never uploads/commits the DAT itself anywhere; it only
//                         copies operator-supplied bytes into the disposable acceptance stack's own
//                         protected storage. Requires ACE.Cloud.Worker to already be running.
//
// This creates only disposable local-test resources. Creating production schemas, identities, and
// secrets remains the Operator Bootstrap command's job (CONTEXT.md), out of scope here.
var mode = args.Length > 0 ? args[0] : "migrate-and-bootstrap";

return mode switch
{
    "migrate-and-bootstrap" => await MigrateAndBootstrapAsync(),
    "validate-external-connection" => await ValidateExternalConnectionAsync(args),
    "prepare-colocated" => await PrepareColocatedAsync(),
    "purge-colocated" => await PurgeColocatedAsync(),
    "activate-portal-dat" => await ActivatePortalDatAsync(args),
    _ => Unknown(mode),
};

static int Unknown(string mode)
{
    Console.Error.WriteLine(
        $"Unknown mode '{mode}'. Expected 'migrate-and-bootstrap', 'validate-external-connection', " +
        "'prepare-colocated', 'purge-colocated', or 'activate-portal-dat'.");
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

static async Task<int> PrepareColocatedAsync()
{
    var adminConnectionString = RequireEnvironment("ACE_CLOUD_ACCEPTANCE_ADMIN_CONNECTION_STRING");
    var runtimeConnectionString = RequireEnvironment("ACE_CLOUD_ACCEPTANCE_RUNTIME_CONNECTION_STRING");
    if (adminConnectionString is null || runtimeConnectionString is null)
    {
        return 1;
    }

    MySqlConnectionStringBuilder adminBuilder;
    MySqlConnectionStringBuilder runtimeBuilder;
    try
    {
        adminBuilder = new MySqlConnectionStringBuilder(adminConnectionString);
        runtimeBuilder = new MySqlConnectionStringBuilder(runtimeConnectionString);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"Invalid co-located acceptance connection string: {ex.Message}");
        return 1;
    }

    if (!string.Equals(adminBuilder.Database, "ace_shard", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("The acceptance admin connection must target the disposable ace_shard schema.");
        return 1;
    }

    if (!string.Equals(runtimeBuilder.Database, "ace_cloud", StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(adminBuilder.Server, runtimeBuilder.Server, StringComparison.OrdinalIgnoreCase) ||
        adminBuilder.Port != runtimeBuilder.Port)
    {
        Console.Error.WriteLine("The ace_cloud runtime connection must target the same MySQL/MariaDB server and port as ace_shard.");
        return 1;
    }

    if (!Regex.IsMatch(runtimeBuilder.UserID, "^[A-Za-z0-9_]{1,32}$") ||
        string.Equals(runtimeBuilder.UserID, "root", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("The disposable Cloud runtime user must be a non-root name containing only letters, digits, and underscores.");
        return 1;
    }

    var runtimeAccount = $"'{runtimeBuilder.UserID}'@'%'";
    var escapedPassword = MySqlHelper.EscapeString(runtimeBuilder.Password);

    adminBuilder.Database = "";
    await using (var admin = new MySqlConnection(adminBuilder.ConnectionString))
    {
        await admin.OpenAsync();
        await ExecuteAsync(admin, "CREATE DATABASE IF NOT EXISTS ace_cloud CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        await ExecuteAsync(admin, $"CREATE USER IF NOT EXISTS {runtimeAccount} IDENTIFIED BY '{escapedPassword}';");
        await ExecuteAsync(admin, $"ALTER USER {runtimeAccount} IDENTIFIED BY '{escapedPassword}';");
    }

    var migrationBuilder = new MySqlConnectionStringBuilder(adminBuilder.ConnectionString) { Database = "ace_cloud" };
    Environment.SetEnvironmentVariable("ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING", migrationBuilder.ConnectionString);
    var migrationResult = await MigrateAndBootstrapAsync();
    if (migrationResult != 0)
    {
        return migrationResult;
    }

    await using (var admin = new MySqlConnection(adminBuilder.ConnectionString))
    {
        await admin.OpenAsync();
        await ExecuteAsync(admin, $"GRANT SELECT, INSERT, UPDATE, DELETE ON ace_cloud.* TO {runtimeAccount};");
    }

    await using (var runtime = new MySqlConnection(runtimeBuilder.ConnectionString))
    {
        await runtime.OpenAsync();
        await ExecuteAsync(runtime, "SELECT 1;");
    }

    Console.WriteLine("Co-located ace_cloud schema and restricted runtime identity are ready.");
    return 0;
}

static async Task<int> PurgeColocatedAsync()
{
    var adminConnectionString = RequireEnvironment("ACE_CLOUD_ACCEPTANCE_ADMIN_CONNECTION_STRING");
    if (adminConnectionString is null)
    {
        return 1;
    }

    var builder = new MySqlConnectionStringBuilder(adminConnectionString);
    if (!string.Equals(builder.Database, "ace_shard", StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine("Refusing purge: the admin connection does not target ace_shard.");
        return 1;
    }

    builder.Database = "";
    await using var admin = new MySqlConnection(builder.ConnectionString);
    await admin.OpenAsync();

    foreach (var trigger in new[]
    {
        "trg_biota_position_reject_cloud_custodied_update",
        "trg_biota_position_reject_cloud_custodied_insert",
        "trg_biota_iid_reject_cloud_custodied_update",
        "trg_biota_iid_reject_cloud_custodied_insert",
        "trg_biota_reject_delete_when_cloud_custodied",
    })
    {
        await ExecuteAsync(admin, $"DROP TRIGGER IF EXISTS ace_shard.{trigger};");
    }

    await ExecuteAsync(admin, "DROP DATABASE IF EXISTS ace_cloud;");
    Console.WriteLine("Disposable ace_cloud schema and its known ace_shard boundary triggers were removed.");
    return 0;
}

static async Task<int> ActivatePortalDatAsync(string[] args)
{
    if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Usage: activate-portal-dat <path-to-client_portal.dat>");
        return 1;
    }

    var datPath = args[1];
    if (!File.Exists(datPath))
    {
        Console.Error.WriteLine($"No file found at '{datPath}'.");
        return 1;
    }

    var connectionString = RequireEnvironment("ACE_CLOUD_ACCEPTANCE_CONNECTION_STRING");
    var shardId = RequireEnvironment("ACE_CLOUD_ACCEPTANCE_SHARD_ID");
    var assetStorageRoot = RequireEnvironment("ACE_CLOUD_ACCEPTANCE_ASSET_STORAGE_ROOT");
    if (connectionString is null || shardId is null || assetStorageRoot is null)
    {
        return 1;
    }

    const uint acceptanceAdminAccountId = 1;
    const int chunkSizeBytes = 8 * 1024 * 1024;

    var fileInfo = new FileInfo(datPath);

    Console.WriteLine($"Computing the checksum of {fileInfo.Length:N0} bytes...");
    string checksumHex;
    await using (var checksumStream = File.OpenRead(datPath))
    {
        checksumHex = Convert.ToHexStringLower(await SHA256.HashDataAsync(checksumStream));
    }

    var storageOptions = new CloudAssetStorageOptions { RootDirectory = assetStorageRoot };
    var blobStore = new LocalProtectedAssetBlobStore(storageOptions);
    await using var context = new CloudDbContext(CloudDbContextOptionsFactory.Create(connectionString));
    var boundary = new CloudAssetImportBoundary(context, blobStore, storageOptions);

    Console.WriteLine("Starting (or resuming) the Asset Import session...");
    var createOutcome = await boundary.CreateOrResumeSessionAsync(
        shardId, CloudAssetKind.Portal, acceptanceAdminAccountId, fileInfo.Length, chunkSizeBytes, checksumHex);
    if (createOutcome.Kind != CloudBoundaryOutcomeKind.Committed)
    {
        Console.Error.WriteLine($"Could not start the Asset Import session: {createOutcome.Reason}");
        return 1;
    }

    var session = createOutcome.Value!;

    if (session.State == CloudAssetImportSessionState.Uploading)
    {
        await using var uploadStream = File.OpenRead(datPath);
        var buffer = new byte[chunkSizeBytes];

        for (var chunkIndex = session.ReceivedChunkCount; chunkIndex < session.ChunkCount; chunkIndex++)
        {
            uploadStream.Seek((long)chunkIndex * chunkSizeBytes, SeekOrigin.Begin);
            var bytesRead = await uploadStream.ReadAsync(buffer.AsMemory(0, chunkSizeBytes));

            var chunkOutcome = await boundary.ApplyChunkAsync(session.Id, chunkIndex, buffer.AsMemory(0, bytesRead));
            if (chunkOutcome.Kind != CloudBoundaryOutcomeKind.Committed)
            {
                Console.Error.WriteLine($"Chunk {chunkIndex} was rejected: {chunkOutcome.Reason}");
                return 1;
            }

            Console.Write($"\rUploaded chunk {chunkIndex + 1}/{session.ChunkCount}...");
        }

        Console.WriteLine();
        Console.WriteLine("Verifying checksum and queuing for staging...");

        var finalizeOutcome = await boundary.FinalizeUploadAsync(session.Id);
        if (finalizeOutcome.Kind != CloudBoundaryOutcomeKind.Committed)
        {
            Console.Error.WriteLine($"Finalization failed: {finalizeOutcome.Reason}");
            return 1;
        }

        session = finalizeOutcome.Value!;
    }
    else
    {
        Console.WriteLine($"Session {session.Id} is already past uploading (state {session.State}); skipping re-upload.");
    }

    if (session.State == CloudAssetImportSessionState.ChecksumFailed)
    {
        Console.Error.WriteLine(
            "Checksum verification failed: the uploaded bytes did not match the file's own computed checksum. " +
            "This should not normally happen; re-run this command to retry.");
        return 1;
    }

    Console.WriteLine("Waiting for the running ACE.Cloud.Worker process's staging worker to extract this DAT (this can take a while for a large file)...");
    var deadlineUtc = DateTime.UtcNow.AddMinutes(15);

    while (true)
    {
        var current = await context.CloudAssetImportSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);

        if (current.State == CloudAssetImportSessionState.StagingComplete && current.ManifestId is { } manifestId)
        {
            var manifest = await context.CloudAssetManifests.AsNoTracking().SingleAsync(m => m.Id == manifestId);

            Console.WriteLine($"Staged manifest version {manifest.Version} with {manifest.EntryCount} entries; activating...");

            var activateOutcome = await boundary.ActivateManifestAsync(shardId, CloudAssetKind.Portal, manifest.Version, acceptanceAdminAccountId);
            if (activateOutcome.Kind != CloudBoundaryOutcomeKind.Committed)
            {
                Console.Error.WriteLine($"Activation failed: {activateOutcome.Reason}");
                return 1;
            }

            Console.WriteLine($"client_portal.dat manifest version {manifest.Version} is now active for shard '{shardId}'.");
            return 0;
        }

        if (current.State == CloudAssetImportSessionState.StagingFailed)
        {
            Console.Error.WriteLine($"Staging failed: {current.ErrorMessage}");
            return 1;
        }

        if (DateTime.UtcNow > deadlineUtc)
        {
            Console.Error.WriteLine(
                "Timed out waiting for staging to complete. Is ACE.Cloud.Worker running and pointed at this same database?");
            return 1;
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

static string? RequireEnvironment(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (!string.IsNullOrWhiteSpace(value))
    {
        return value;
    }

    Console.Error.WriteLine($"{name} is not set by the local acceptance launcher.");
    return null;
}

static async Task ExecuteAsync(MySqlConnection connection, string commandText)
{
    await using var command = connection.CreateCommand();
    command.CommandText = commandText;
    await command.ExecuteNonQueryAsync();
}
