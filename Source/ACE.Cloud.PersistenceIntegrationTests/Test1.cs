using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green smoke test for issue #1: boots a disposable MariaDB instance, applies ACE's
/// existing Auth/Shard/World schemas plus the empty versioned Cloud schema, and proves the Cloud
/// Shard binding invariant (ARCH-001) is enforced by the database itself, not only application
/// code.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodyDatabaseSmokeTests
{
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
        // Every test starts from an empty singleton table so method order never affects outcome.
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CloudShardBinding;";
        await command.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task ExistingAceSchemas_AndEmptyCloudSchema_AreAllApplied()
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var schemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA " +
                "WHERE SCHEMA_NAME IN ('ace_auth', 'ace_shard', 'ace_world', 'ace_cloud');";

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                schemas.Add(reader.GetString(0));
            }
        }

        CollectionAssert.AreEquivalent(
            new[] { "ace_auth", "ace_shard", "ace_world", "ace_cloud" },
            schemas.ToArray());
    }

    [TestMethod]
    public async Task CloudShardBinding_RequiresNonEmptyShardId()
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CloudShardBinding (Id, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion)
            VALUES (1, NULL, '0.1.0', '0.1.0', '0.1.0');
            """;

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(() => command.ExecuteNonQueryAsync());
        StringAssert.Contains(exception.Message, "ShardId");
    }

    [TestMethod]
    public async Task CloudShardBinding_RejectsMoreThanOneShardPerDeployment()
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using (var insertFirst = connection.CreateCommand())
        {
            insertFirst.CommandText = """
                INSERT INTO CloudShardBinding (Id, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion)
                VALUES (1, 'us1', '0.1.0', '0.1.0', '0.1.0');
                """;
            await insertFirst.ExecuteNonQueryAsync();
        }

        await using var insertSecond = connection.CreateCommand();
        insertSecond.CommandText = """
            INSERT INTO CloudShardBinding (Id, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion)
            VALUES (2, 'us2', '0.1.0', '0.1.0', '0.1.0');
            """;

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(() => insertSecond.ExecuteNonQueryAsync());
        StringAssert.Contains(exception.Message, "CK_CloudShardBinding_Singleton");
    }

    [TestMethod]
    public async Task CloudDbContext_PersistsAndReadsBackTheShardBinding()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        await using (var writeContext = new CloudDbContext(options))
        {
            writeContext.CloudShardBindings.Add(new CloudShardBinding(
                shardId: "us1",
                schemaVersion: CloudSchemaInfo.CurrentVersion,
                aceExtensionVersion: CloudDatabaseFixture.AceExtensionVersion,
                contractProtocolVersion: CloudDatabaseFixture.ContractProtocolVersion));

            await writeContext.SaveChangesAsync();
        }

        await using var readContext = new CloudDbContext(options);
        var binding = await readContext.CloudShardBindings.SingleAsync();

        Assert.AreEqual("us1", binding.ShardId);
        Assert.AreEqual(CloudSchemaInfo.CurrentVersion, binding.SchemaVersion);
        Assert.AreEqual(CloudDatabaseFixture.AceExtensionVersion, binding.AceExtensionVersion);
        Assert.AreEqual(CloudDatabaseFixture.ContractProtocolVersion, binding.ContractProtocolVersion);
    }
}
