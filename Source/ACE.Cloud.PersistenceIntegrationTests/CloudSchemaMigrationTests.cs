using ACE.Cloud.Persistence.Migrations;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Proves the acceptance criterion "Migration apply and rollback behavior is repeatable against a
/// disposable ACE database": <see cref="CloudSchemaMigrator"/> can roll a disposable Cloud schema
/// forward and backward repeatedly without leaving stray state, and unrelated data (the
/// CloudShardBinding row from an earlier migration) survives rolling back a later one.
///
/// This runs as one sequential scenario rather than several independent [TestMethod]s: every step
/// mutates the same schema's DDL, which is not safely interleavable the way per-row data tests are.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudSchemaMigrationTests
{
    private const string InitialMigrationId = "20260827000001_InitialCloudSchema";

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

    [TestMethod]
    public async Task MigrationLifecycle_ApplyRollbackReapply_IsRepeatable()
    {
        var connectionString = _fixture.CloudConnectionString;

        // The fixture's startup already applied every migration; confirm that baseline.
        await AssertAppliedAsync(connectionString, "20260827000002_AddCloudCustodyRecords");
        await AssertAppliedAsync(connectionString, "20260827000003_ProtectCloudCustodyBiotaFromDeletion");
        await AssertAppliedAsync(connectionString, "20260827000004_AddIdempotencyAndLedgerOutbox");
        await AssertAppliedAsync(connectionString, "20260827000005_AddCloudStackLots");
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudCustodyRecord"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudShardBinding"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudIdempotencyRecord"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudActivityLedgerEvent"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudCustodyOutboxEvent"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudStackLot"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudStackLotLineageEvent"));

        var shardId = await SeedShardBindingAsync(connectionString);

        for (var repetition = 0; repetition < 2; repetition++)
        {
            // Roll back to the baseline: the custody table and its triggers must disappear...
            await CloudSchemaMigrator.RollbackToAsync(connectionString, InitialMigrationId);

            Assert.IsFalse(
                await TableExistsAsync(connectionString, "CloudCustodyRecord"),
                $"CloudCustodyRecord must not exist after rollback (repetition {repetition}).");
            Assert.IsFalse(await TableExistsAsync(connectionString, "CloudIdempotencyRecord"));
            Assert.IsFalse(await TableExistsAsync(connectionString, "CloudActivityLedgerEvent"));
            Assert.IsFalse(await TableExistsAsync(connectionString, "CloudCustodyOutboxEvent"));
            Assert.IsFalse(await TableExistsAsync(connectionString, "CloudStackLot"));
            Assert.IsFalse(await TableExistsAsync(connectionString, "CloudStackLotLineageEvent"));

            // ...while unrelated data from the still-applied migration is untouched.
            Assert.IsTrue(await TableExistsAsync(connectionString, "CloudShardBinding"));
            Assert.AreEqual(shardId, await ReadShardIdAsync(connectionString));

            // Re-apply: the custody table and its exclusivity guard must both come back.
            await CloudSchemaMigrator.MigrateAsync(connectionString);

            Assert.IsTrue(await TableExistsAsync(connectionString, "CloudCustodyRecord"));
            Assert.IsTrue(await TableExistsAsync(connectionString, "CloudIdempotencyRecord"));
            Assert.IsTrue(await TableExistsAsync(connectionString, "CloudActivityLedgerEvent"));
            Assert.IsTrue(await TableExistsAsync(connectionString, "CloudCustodyOutboxEvent"));
            Assert.IsTrue(await TableExistsAsync(connectionString, "CloudStackLot"));
            Assert.IsTrue(await TableExistsAsync(connectionString, "CloudStackLotLineageEvent"));
            await AssertAppliedAsync(connectionString, "20260827000002_AddCloudCustodyRecords");
            await AssertAppliedAsync(connectionString, "20260827000003_ProtectCloudCustodyBiotaFromDeletion");
            await AssertAppliedAsync(connectionString, "20260827000004_AddIdempotencyAndLedgerOutbox");
            await AssertAppliedAsync(connectionString, "20260827000005_AddCloudStackLots");
            await AssertCustodySchemaIsFunctionalAsync(connectionString, repetition);
        }

        // Full rollback removes everything this migrator owns, including the baseline table.
        await CloudSchemaMigrator.RollbackToAsync(connectionString, targetMigrationId: null);
        Assert.IsFalse(await TableExistsAsync(connectionString, "CloudCustodyRecord"));
        Assert.IsFalse(await TableExistsAsync(connectionString, "CloudShardBinding"));
        Assert.IsFalse(await TableExistsAsync(connectionString, "CloudIdempotencyRecord"));
        Assert.IsFalse(await TableExistsAsync(connectionString, "CloudActivityLedgerEvent"));
        Assert.IsFalse(await TableExistsAsync(connectionString, "CloudCustodyOutboxEvent"));
        Assert.IsFalse(await TableExistsAsync(connectionString, "CloudStackLot"));
        Assert.IsFalse(await TableExistsAsync(connectionString, "CloudStackLotLineageEvent"));

        // Re-applying from empty must be just as repeatable as the partial case above.
        await CloudSchemaMigrator.MigrateAsync(connectionString);
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudCustodyRecord"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudShardBinding"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudIdempotencyRecord"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudActivityLedgerEvent"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudCustodyOutboxEvent"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudStackLot"));
        Assert.IsTrue(await TableExistsAsync(connectionString, "CloudStackLotLineageEvent"));
        await AssertAppliedAsync(connectionString, InitialMigrationId);
        await AssertAppliedAsync(connectionString, "20260827000002_AddCloudCustodyRecords");
        await AssertAppliedAsync(connectionString, "20260827000003_ProtectCloudCustodyBiotaFromDeletion");
        await AssertAppliedAsync(connectionString, "20260827000004_AddIdempotencyAndLedgerOutbox");
        await AssertAppliedAsync(connectionString, "20260827000005_AddCloudStackLots");
    }

    private static async Task<string> SeedShardBindingAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CloudShardBinding (Id, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion)
            VALUES (1, 'us1', '0.1.0', '0.1.0', '0.1.0');
            """;
        await command.ExecuteNonQueryAsync();

        return "us1";
    }

    private static async Task<string?> ReadShardIdAsync(string connectionString)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ShardId FROM CloudShardBinding WHERE Id = 1;";

        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task AssertCustodySchemaIsFunctionalAsync(string connectionString, int repetition)
    {
        var biotaId = 950_000u + (uint)repetition * 10;
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        // A valid deposit succeeds against the re-applied schema...
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = """
                INSERT INTO CloudCustodyRecord (Id, BiotaId, ShardId, OwnerId, LedgerCorrelationId, Version)
                VALUES (@id, @biotaId, 'us1', @ownerId, @ledgerCorrelationId, 1);
                """;
            insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("@biotaId", biotaId);
            insert.Parameters.AddWithValue("@ownerId", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("@ledgerCorrelationId", Guid.NewGuid().ToString());
            await insert.ExecuteNonQueryAsync();
        }

        // ...and the exclusivity guard is enforced again, not just the bare table shape.
        var possessedBiotaId = biotaId + 1;
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, possessedBiotaId);
        await AceShardTestData.GrantContainerAsync(_fixture.AceShardConnectionString, possessedBiotaId, containerId: possessedBiotaId + 1000);

        await using var conflicting = connection.CreateCommand();
        conflicting.CommandText = """
            INSERT INTO CloudCustodyRecord (Id, BiotaId, ShardId, OwnerId, LedgerCorrelationId, Version)
            VALUES (@id, @biotaId, 'us1', @ownerId, @ledgerCorrelationId, 1);
            """;
        conflicting.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        conflicting.Parameters.AddWithValue("@biotaId", possessedBiotaId);
        conflicting.Parameters.AddWithValue("@ownerId", Guid.NewGuid().ToString());
        conflicting.Parameters.AddWithValue("@ledgerCorrelationId", Guid.NewGuid().ToString());

        await Assert.ThrowsExactlyAsync<MySqlException>(() => conflicting.ExecuteNonQueryAsync());

        // ...and issue #3's delete-protection trigger is enforced again, not just the insert guard.
        await using var deleteConnection = new MySqlConnection(_fixture.AceShardConnectionString);
        await deleteConnection.OpenAsync();
        await using var delete = deleteConnection.CreateCommand();
        delete.CommandText = "DELETE FROM biota WHERE id = @biotaId;";
        delete.Parameters.AddWithValue("@biotaId", biotaId);

        var deleteException = await Assert.ThrowsExactlyAsync<MySqlException>(() => delete.ExecuteNonQueryAsync());
        StringAssert.Contains(deleteException.Message, "Cloud custody");
    }

    private static async Task AssertAppliedAsync(string connectionString, string migrationId)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM CloudSchemaMigrationHistory WHERE MigrationId = @migrationId;";
        command.Parameters.AddWithValue("@migrationId", migrationId);

        var count = (long)(await command.ExecuteScalarAsync())!;
        Assert.AreEqual(1, count, $"Expected migration '{migrationId}' to be recorded as applied.");
    }

    private static async Task<bool> TableExistsAsync(string connectionString, string tableName)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM information_schema.TABLES
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @tableName;
            """;
        command.Parameters.AddWithValue("@tableName", tableName);

        var count = (long)(await command.ExecuteScalarAsync())!;
        return count > 0;
    }
}
