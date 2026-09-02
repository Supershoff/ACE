using ACE.Cloud.Persistence.Migrations;
using MySqlConnector;
using Testcontainers.MariaDb;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Boots one disposable MariaDB instance per test class, applies ACE's existing Auth/Shard/World
/// schemas from Database/Base plus the versioned Cloud schema migrations, and tears everything down
/// unconditionally. Nothing here is reused between test runs: every run gets a fresh container
/// and fresh databases, and a failed setup disposes the container before propagating the error.
/// </summary>
public sealed class CloudDatabaseFixture : IAsyncDisposable
{
    public const string AceExtensionVersion = "0.1.0";
    public const string ContractProtocolVersion = "0.1.0";

    private const string CloudSchemaName = "ace_cloud";
    private const string ShardSchemaName = "ace_shard";
    private const string AuthSchemaName = "ace_auth";

    private readonly MariaDbContainer _container;

    private CloudDatabaseFixture(MariaDbContainer container)
    {
        _container = container;
    }

    public string CloudConnectionString => BuildConnectionString(CloudSchemaName);

    /// <summary>
    /// A connection string to ACE's own shard database, scoped to the same disposable server as
    /// <see cref="CloudConnectionString"/>. Only Red/Green tests proving the world-boundary
    /// invariants (ARCH-005) should use this directly; ordinary Cloud application code must never
    /// hold a connection like this (ARCH-004).
    /// </summary>
    public string AceShardConnectionString => BuildConnectionString(ShardSchemaName);

    /// <summary>
    /// A connection string to ACE's own auth database, scoped to the same disposable server as
    /// <see cref="CloudConnectionString"/>. Only Red/Green tests proving ARCH-004's "cannot update
    /// auth password fields" invariant should use this directly.
    /// </summary>
    public string AceAuthConnectionString => BuildConnectionString(AuthSchemaName);

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
            await fixture.ApplyVersionedCloudSchemaAsync();
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

    /// <summary>
    /// Provisions a MariaDB identity granted every privilege on the Cloud schema (ace_cloud) plus
    /// issue #39's minimum narrow read-only grants -- SELECT on exactly <c>ace_shard.character</c> and
    /// <c>ace_shard.biota_properties_i_i_d</c>, nothing else in ace_shard -- and returns a connection
    /// string authenticating as it. This models the real narrowly privileged companion web database
    /// identity <see cref="ACE.Cloud.LocalAcceptanceMigrator"/>'s <c>prepare-colocated</c> mode now
    /// provisions and ARCH-004 requires ("The web database identity MUST NOT have native-biota write
    /// privileges" -- a scoped read is not a write); tests use it to prove both halves of that shape
    /// are enforced database grants, not only documentation. <paramref name="username"/> and
    /// <paramref name="password"/> are caller-generated test-only random tokens, not real operator
    /// secrets.
    /// </summary>
    public async Task<string> CreateRestrictedCompanionConnectionStringAsync(string username, string password)
    {
        await using var connection = new MySqlConnection(BuildConnectionString(database: null));
        await connection.OpenAsync();

        await using (var createUser = connection.CreateCommand())
        {
            createUser.CommandText = $"CREATE USER IF NOT EXISTS '{username}'@'%' IDENTIFIED BY '{password}';";
            await createUser.ExecuteNonQueryAsync();
        }

        await using (var grant = connection.CreateCommand())
        {
            grant.CommandText = $"GRANT ALL PRIVILEGES ON `{CloudSchemaName}`.* TO '{username}'@'%';";
            await grant.ExecuteNonQueryAsync();
        }

        await using (var grantCharacter = connection.CreateCommand())
        {
            grantCharacter.CommandText = $"GRANT SELECT ON `{ShardSchemaName}`.`character` TO '{username}'@'%';";
            await grantCharacter.ExecuteNonQueryAsync();
        }

        await using (var grantMonarchProperty = connection.CreateCommand())
        {
            grantMonarchProperty.CommandText = $"GRANT SELECT ON `{ShardSchemaName}`.`biota_properties_i_i_d` TO '{username}'@'%';";
            await grantMonarchProperty.ExecuteNonQueryAsync();
        }

        await using (var flush = connection.CreateCommand())
        {
            flush.CommandText = "FLUSH PRIVILEGES;";
            await flush.ExecuteNonQueryAsync();
        }

        var builder = new MySqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = CloudSchemaName,
            UserID = username,
            Password = password,
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Provisions the same identity as <see cref="CreateRestrictedCompanionConnectionStringAsync"/>
    /// except without the ace_shard.character/biota_properties_i_i_d grants -- the exact broken shape
    /// issue #39's local acceptance provisioning had before its fix (ace_cloud only). Negative-path
    /// tests use this to prove the missing-grant failure mode is both reproducible and reported through
    /// <see cref="ACE.Cloud.Persistence.CloudDatabasePrivilegeException"/> rather than an unhandled,
    /// detail-leaking <see cref="MySqlException"/>.
    /// </summary>
    public async Task<string> CreateRestrictedCompanionConnectionStringWithoutShardAccessAsync(string username, string password)
    {
        await using var connection = new MySqlConnection(BuildConnectionString(database: null));
        await connection.OpenAsync();

        await using (var createUser = connection.CreateCommand())
        {
            createUser.CommandText = $"CREATE USER IF NOT EXISTS '{username}'@'%' IDENTIFIED BY '{password}';";
            await createUser.ExecuteNonQueryAsync();
        }

        await using (var grant = connection.CreateCommand())
        {
            grant.CommandText = $"GRANT ALL PRIVILEGES ON `{CloudSchemaName}`.* TO '{username}'@'%';";
            await grant.ExecuteNonQueryAsync();
        }

        await using (var flush = connection.CreateCommand())
        {
            flush.CommandText = "FLUSH PRIVILEGES;";
            await flush.ExecuteNonQueryAsync();
        }

        var builder = new MySqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = CloudSchemaName,
            UserID = username,
            Password = password,
        };

        return builder.ConnectionString;
    }

    /// <summary>
    /// Provisions a MariaDB identity granted only <c>SELECT</c> on <c>ace_auth.account</c> and
    /// nothing else, and returns a connection string authenticating as it. This models the narrowly
    /// privileged read-only identity <c>AuthBridgeOptions.AceAuthConnectionString</c>'s doc comment
    /// requires (AUTH-002: the Auth Bridge reuses ACE's own password verifier and so needs read
    /// access to the hash/salt, but must never be able to write them); tests use it to prove that
    /// restriction is an enforced database grant, distinct from the Backend/Worker's
    /// <see cref="CreateRestrictedCompanionConnectionStringAsync"/> identity, which has no
    /// <c>ace_auth</c> access at all. <paramref name="username"/> and <paramref name="password"/>
    /// are caller-generated test-only random tokens, not real operator secrets.
    /// </summary>
    public async Task<string> CreateRestrictedAuthBridgeConnectionStringAsync(string username, string password)
    {
        await using var connection = new MySqlConnection(BuildConnectionString(database: null));
        await connection.OpenAsync();

        await using (var createUser = connection.CreateCommand())
        {
            createUser.CommandText = $"CREATE USER IF NOT EXISTS '{username}'@'%' IDENTIFIED BY '{password}';";
            await createUser.ExecuteNonQueryAsync();
        }

        await using (var grant = connection.CreateCommand())
        {
            grant.CommandText = $"GRANT SELECT ON `{AuthSchemaName}`.`account` TO '{username}'@'%';";
            await grant.ExecuteNonQueryAsync();
        }

        await using (var flush = connection.CreateCommand())
        {
            flush.CommandText = "FLUSH PRIVILEGES;";
            await flush.ExecuteNonQueryAsync();
        }

        var builder = new MySqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = AuthSchemaName,
            UserID = username,
            Password = password,
        };

        return builder.ConnectionString;
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

    private async Task ApplyVersionedCloudSchemaAsync()
    {
        await using (var connection = new MySqlConnection(BuildConnectionString(database: null)))
        {
            await connection.OpenAsync();

            await using var createDatabase = connection.CreateCommand();
            createDatabase.CommandText = $"CREATE DATABASE IF NOT EXISTS `{CloudSchemaName}` DEFAULT CHARACTER SET utf8mb4;";
            await createDatabase.ExecuteNonQueryAsync();
        }

        await CloudSchemaMigrator.MigrateAsync(CloudConnectionString);
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
