using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Shared per-test reset for the world-boundary handoff test classes (issue #4): clears every Cloud
/// table these tests touch and reinserts a fresh singleton <c>CloudShardBinding</c> row, so each
/// [TestMethod] starts from the same known-empty state regardless of run order.
/// </summary>
internal static class CloudBoundaryTestFixtureData
{
    private static readonly string[] TablesInDeleteOrder =
    [
        "CloudIdempotencyRecord",
        "CloudActivityLedgerEvent",
        "CloudCustodyOutboxEvent",
        "CloudCustodyRecord",
        "CloudShardBinding",
    ];

    public static async Task ResetAsync(string cloudConnectionString, string shardId)
    {
        await using var connection = new MySqlConnection(cloudConnectionString);
        await connection.OpenAsync();

        foreach (var table in TablesInDeleteOrder)
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = $"DELETE FROM {table};";
            await delete.ExecuteNonQueryAsync();
        }

        await using var insertBinding = connection.CreateCommand();
        insertBinding.CommandText = """
            INSERT INTO CloudShardBinding (Id, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion)
            VALUES (1, @shardId, '0.1.0', '0.1.0', '0.1.0');
            """;
        insertBinding.Parameters.AddWithValue("@shardId", shardId);
        await insertBinding.ExecuteNonQueryAsync();
    }
}
