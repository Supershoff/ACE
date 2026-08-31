using MySqlConnector;

namespace ACE.Cloud.Persistence.Migrations;

/// <summary>
/// Applies and rolls back the Cloud schema's versioned migrations (OPS-002) using plain
/// MySqlConnector ADO.NET rather than EF Core's migrator. This environment has no access to the
/// dotnet-ef design-time tool, and EF Core's runtime migration discovery
/// (<c>Database.MigrateAsync</c>) could not be made to find hand-authored
/// <see cref="Microsoft.EntityFrameworkCore.Migrations.Migration"/> subclasses with the installed
/// Microsoft.EntityFrameworkCore/Pomelo.EntityFrameworkCore.MySql versions in this environment
/// (IMigrationsAssembly.Migrations stayed empty despite the types satisfying every documented
/// discovery condition); shipping that unverified was riskier than a small, fully self-tested
/// migration runner. Applied migration IDs are tracked in CloudSchemaMigrationHistory, the same
/// role EF Core's own __EFMigrationsHistory table plays.
/// </summary>
public static class CloudSchemaMigrator
{
    private const string HistoryTable = "CloudSchemaMigrationHistory";

    private static readonly IReadOnlyList<CloudSchemaMigrationStep> OrderedSteps =
    [
        new InitialCloudSchema(),
        new AddCloudCustodyRecords(),
        new ProtectCloudCustodyBiotaFromDeletion(),
        new AddIdempotencyAndLedgerOutbox(),
        new AddCloudStackLots(),
        new AddWithdrawalReservationsAndOrderedOutbox(),
        new AddCloudCustodianConfiguration(),
        new AddCloudFrozenEnchantments(),
        new AddCloudPyrealRemainder(),
        new AddLayerIdToCloudFrozenEnchantment(),
        new AddWithdrawalRedemptionSupport(),
        new AddIdentityAllegianceOutboxAndVaultGuard(),
        new AddCloudWebSessionsAndGrantConsumption(),
        new AddAccountLinkingAndDisplayCharacter(),
        new AddAssetImportPipeline(),
        new AddCloudIconDiagnostics(),
        new AddCloudIconDiagnosticManifestCorrelation(),
        new UnifyWithdrawalReservationTargets(),
        new AddProjectionCheckpointsDeadLettersAndLiveStream(),
        new AddQuotasMaintenanceAndMarketplaceState(),
        new AddCloudStorageQuotaOwnerLock(),
        new AddCloudInventoryItemPropertiesProjection(),
        new AddCloudSearchConfiguration(),
        new AddCloudNotification(),
        new AddAppraisalSnapshotAndIconCompositionInputs(),
        new AddIconCompositionSharedOverlayDids(),
        new AddCloudTransferOffers(),
    ];

    /// <summary>
    /// Applies every migration not yet recorded in <see cref="HistoryTable"/>, in order.
    /// </summary>
    public static async Task MigrateAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureHistoryTableAsync(connection, cancellationToken);
        var applied = await GetAppliedMigrationIdsAsync(connection, cancellationToken);

        foreach (var step in OrderedSteps)
        {
            if (applied.Contains(step.Id))
            {
                continue;
            }

            foreach (var statement in step.UpStatements)
            {
                await ExecuteAsync(connection, statement, cancellationToken);
            }

            await RecordAppliedAsync(connection, step.Id, cancellationToken);
        }
    }

    /// <summary>
    /// Rolls back every applied migration after <paramref name="targetMigrationId"/> (exclusive),
    /// most recent first. Pass null to roll back every migration.
    /// </summary>
    public static async Task RollbackToAsync(
        string connectionString, string? targetMigrationId, CancellationToken cancellationToken = default)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await EnsureHistoryTableAsync(connection, cancellationToken);
        var applied = await GetAppliedMigrationIdsAsync(connection, cancellationToken);

        for (var i = OrderedSteps.Count - 1; i >= 0; i--)
        {
            var step = OrderedSteps[i];

            if (targetMigrationId is not null && string.CompareOrdinal(step.Id, targetMigrationId) <= 0)
            {
                break;
            }

            if (!applied.Contains(step.Id))
            {
                continue;
            }

            foreach (var statement in step.DownStatements)
            {
                await ExecuteAsync(connection, statement, cancellationToken);
            }

            await RemoveAppliedAsync(connection, step.Id, cancellationToken);
        }
    }

    private static async Task EnsureHistoryTableAsync(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            connection,
            $"""
            CREATE TABLE IF NOT EXISTS {HistoryTable} (
                MigrationId VARCHAR(150) NOT NULL,
                AppliedAtUtc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                PRIMARY KEY (MigrationId)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
            """,
            cancellationToken);
    }

    private static async Task<HashSet<string>> GetAppliedMigrationIdsAsync(
        MySqlConnection connection, CancellationToken cancellationToken)
    {
        var applied = new HashSet<string>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT MigrationId FROM {HistoryTable};";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    private static async Task RecordAppliedAsync(MySqlConnection connection, string migrationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {HistoryTable} (MigrationId) VALUES (@migrationId);";
        command.Parameters.AddWithValue("@migrationId", migrationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RemoveAppliedAsync(MySqlConnection connection, string migrationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {HistoryTable} WHERE MigrationId = @migrationId;";
        command.Parameters.AddWithValue("@migrationId", migrationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
