using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Testcontainers.MariaDb;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Boots one disposable MariaDB instance per test class, applies ACE's existing Auth/Shard/World
/// schemas from Database/Base plus the empty versioned Cloud schema, and tears everything down
/// unconditionally. Nothing here is reused between test runs: every run gets a fresh container
/// and fresh databases, and a failed setup disposes the container before propagating the error.
/// </summary>
public sealed class CloudDatabaseFixture : IAsyncDisposable
{
    public const string AceExtensionVersion = "0.1.0";
    public const string ContractProtocolVersion = "0.1.0";

    private const string CloudSchemaName = "ace_cloud";

    private readonly MariaDbContainer _container;

    private CloudDatabaseFixture(MariaDbContainer container)
    {
        _container = container;
    }

    public string CloudConnectionString => BuildConnectionString(CloudSchemaName);

    public static async Task<CloudDatabaseFixture> StartAsync()
    {
        var container = new MariaDbBuilder("mariadb:11.4")
            .WithUsername("root")
            .WithPassword("root")
            .Build();

        var fixture = new CloudDatabaseFixture(container);

        try
        {
            await container.StartAsync();
            await fixture.ApplyExistingAceSchemasAsync();
            await fixture.ApplyEmptyVersionedCloudSchemaAsync();
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }

        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task ApplyExistingAceSchemasAsync()
    {
        foreach (var scriptName in new[] { "AuthenticationBase.sql", "ShardBase.sql", "WorldBase.sql" })
        {
            var script = await File.ReadAllTextAsync(FindAceBaseScript(scriptName));

            await using var connection = new MySqlConnection(BuildConnectionString(database: null));
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = script;
            command.CommandTimeout = 120;
            await command.ExecuteNonQueryAsync();
        }
    }

    private async Task ApplyEmptyVersionedCloudSchemaAsync()
    {
        await using (var connection = new MySqlConnection(BuildConnectionString(database: null)))
        {
            await connection.OpenAsync();

            await using var createDatabase = connection.CreateCommand();
            createDatabase.CommandText = $"CREATE DATABASE IF NOT EXISTS `{CloudSchemaName}` DEFAULT CHARACTER SET utf8mb4;";
            await createDatabase.ExecuteNonQueryAsync();
        }

        var options = CloudDbContextOptionsFactory.Create(CloudConnectionString);
        await using var context = new CloudDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

    private string BuildConnectionString(string? database)
    {
        var builder = new MySqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database ?? string.Empty,
        };

        return builder.ConnectionString;
    }

    private static string FindAceBaseScript(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Database", "Base", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Unable to locate {fileName} above {AppContext.BaseDirectory}.");
    }
}
