using System.Reflection;
using System.Threading;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Cloud.PersistenceIntegrationTests;
using ACE.Common;
using ACE.Database;
using ACE.Server.Entity;
using ACE.Server.Managers;
using MySqlConnector;

namespace ACE.Cloud.AceCharacterIntegrationTests;

/// <summary>
/// Red -&gt; Green regression test for issue #17's review, finding 1 (P0): VAULT-005's monarch-deletion
/// guard derived "is this character currently a monarch" from <c>IPlayer.Allegiance</c>, a cache only
/// ever populated by <see cref="AllegianceManager.LoadPlayer"/>, which itself only ever runs for a
/// character that has logged in during this server process's uptime
/// (<c>PlayerManager.SwitchPlayerFromOfflineToOnline</c>). <c>PlayerManager.Initialize()</c> -- the
/// ordinary startup path that populates every offline character -- never calls
/// <see cref="AllegianceManager.LoadPlayer"/>, so for any monarch who has not logged in since the last
/// server restart (the common case on a long-uptime server), the old expression silently evaluated to
/// "not a monarch" and the guard never blocked deletion at all, permanently stranding the Allegiance
/// Vault. <see cref="AllegianceManager.IsMonarch"/> now derives this live via
/// <see cref="AllegianceManager.GetAllegiance"/> instead, which is exactly what this test proves by
/// reproducing the real defect precondition: a monarch character reachable only through
/// <c>PlayerManager</c>'s ordinary offline dictionary, never through <see cref="AllegianceManager.LoadPlayer"/>.
///
/// Neither <c>ACE.Cloud.ServerSeamsTests</c> (deliberately WorldObject-free, per its own doc comment)
/// nor <c>ACE.Cloud.AceCustodyIntegrationTests</c> (deliberately does not reference ACE.Server, per its
/// own doc comment) can exercise this path, so this project exists to drive the real
/// <see cref="PlayerManager"/>/<see cref="OfflinePlayer"/>/<see cref="AllegianceManager"/> production
/// code against a disposable ace_shard database, reusing <see cref="CloudDatabaseFixture"/> rather than
/// duplicating its Testcontainers/schema bootstrap. Its sibling <c>AceShardTestData</c> is internal to
/// <c>ACE.Cloud.PersistenceIntegrationTests</c> and so is not reusable from here either (the same
/// reason <c>ACE.Cloud.AceCustodyIntegrationTests.CloudCustodyAceSeamsTests</c> keeps its own local
/// ace_shard seeding helpers rather than reusing that project's); the minimal seeding this class needs
/// is duplicated locally below.
///
/// <see cref="DatabaseManager.Shard"/> is only ever assigned by the full <see cref="DatabaseManager.Initialize"/>,
/// which additionally requires a populated Auth/World database this disposable fixture does not seed
/// (it validates a "human" weenie exists, among other things). Constructing the exact same production
/// <see cref="ShardDatabase"/> ACE.Server.Program itself wires up and installing it through
/// <see cref="DatabaseManager"/>'s own internal <c>SerializedShardDatabase</c> wrapper (via reflection,
/// since that wrapper's constructor is intentionally internal) is the narrowest way to point that real
/// production code at this fixture's ace_shard without reimplementing any of it or touching production
/// code just to relax that constructor's visibility for a test.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CharacterDeletionMonarchVaultGuardTests
{
    private const string ShardId = "us1";

    private const short PatronPropertyType = 25; // PropertyInstanceId.Patron
    private const short MonarchPropertyType = 26; // PropertyInstanceId.Monarch
    private const short NamePropertyType = 1; // PropertyString.Name
    private const uint AllegianceWeenieType = 30; // WeenieType.Allegiance

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 770_000;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();

        var shardConnection = new MySqlConnectionStringBuilder(_fixture.AceShardConnectionString);
        var authConnection = new MySqlConnectionStringBuilder(_fixture.AceShardConnectionString) { Database = "ace_auth" };
        var cloudConnection = new MySqlConnectionStringBuilder(_fixture.CloudConnectionString);

        var configuration = new MasterConfiguration();
        configuration.MySql.Shard.Host = shardConnection.Server;
        configuration.MySql.Shard.Port = shardConnection.Port;
        configuration.MySql.Shard.Database = shardConnection.Database;
        configuration.MySql.Shard.Username = shardConnection.UserID;
        configuration.MySql.Shard.Password = shardConnection.Password;
        configuration.MySql.Authentication.Host = authConnection.Server;
        configuration.MySql.Authentication.Port = authConnection.Port;
        configuration.MySql.Authentication.Database = authConnection.Database;
        configuration.MySql.Authentication.Username = authConnection.UserID;
        configuration.MySql.Authentication.Password = authConnection.Password;
        configuration.CloudMule.Enabled = true;
        configuration.CloudMule.ShardId = ShardId;
        configuration.MySql.Cloud.Host = cloudConnection.Server;
        configuration.MySql.Cloud.Port = cloudConnection.Port;
        configuration.MySql.Cloud.Database = cloudConnection.Database;
        configuration.MySql.Cloud.Username = cloudConnection.UserID;
        configuration.MySql.Cloud.Password = cloudConnection.Password;

        ConfigManager.Initialize(configuration);

        var shardDatabase = new ShardDatabase();
        var serializedShardDatabaseCtor = typeof(SerializedShardDatabase).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(ShardDatabase) }, null)
            ?? throw new InvalidOperationException("SerializedShardDatabase's internal (ShardDatabase) constructor was not found.");
        var serializedShardDatabase = serializedShardDatabaseCtor.Invoke(new object[] { shardDatabase });

        var shardProperty = typeof(DatabaseManager).GetProperty(nameof(DatabaseManager.Shard), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("DatabaseManager.Shard was not found.");
        shardProperty.GetSetMethod(nonPublic: true)!.Invoke(null, new object?[] { serializedShardDatabase });

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using var insertBinding = connection.CreateCommand();
        insertBinding.CommandText = """
            INSERT INTO CloudShardBinding (Id, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion)
            VALUES (1, @shardId, '0.1.0', '0.1.0', '0.1.0');
            """;
        insertBinding.Parameters.AddWithValue("@shardId", ShardId);
        await insertBinding.ExecuteNonQueryAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);

    /// <summary>
    /// Seeds a real monarch character with one real vassal (both as full ace_shard character/biota
    /// rows, exactly like production characters) plus the persisted Allegiance biota that binds them,
    /// then calls the exact same <see cref="PlayerManager.Initialize"/> entry point ACE.Server.Program
    /// calls at every boot -- the ordinary bulk offline-character load that never calls
    /// <see cref="AllegianceManager.LoadPlayer"/> for anyone.
    /// </summary>
    private static async Task<uint> SeedMonarchNeverLoadedThisSessionAsync()
    {
        var monarchId = NextId();
        var vassalId = NextId();
        var allegianceBiotaId = NextId();

        await InsertCharacterAsync(monarchId, accountId: 1, name: "OldMonarch");
        await InsertBiotaAsync(monarchId);
        await SetNameAsync(monarchId, "OldMonarch");

        await InsertCharacterAsync(vassalId, accountId: 2, name: "LoyalVassal");
        await InsertBiotaAsync(vassalId);
        await SetNameAsync(vassalId, "LoyalVassal");
        await GrantMonarchAsync(vassalId, monarchId);
        // Allegiance.Init/BuildPatronVassals chains vassals under their monarch strictly by PatronId
        // (PropertyInstanceId.Monarch alone is not enough): without this, AllegianceManager.GetAllegiance
        // would see a Members tree containing only the monarch (TotalMembers == 1) and return null.
        await GrantPatronAsync(vassalId, monarchId);

        await InsertAllegianceBiotaAsync(allegianceBiotaId, monarchId);

        // The exact production startup path: bulk-constructs every OfflinePlayer directly from
        // ace_shard, without ever touching AllegianceManager (issue #17 review, finding 1).
        PlayerManager.Initialize();

        return monarchId;
    }

    [TestMethod]
    public async Task IsMonarch_ForAMonarchNeverLoadedThisSession_DerivesTrueFromPersistedAllegiance_NotTheStaleCache()
    {
        var monarchId = await SeedMonarchNeverLoadedThisSessionAsync();

        var offlinePlayer = PlayerManager.GetOfflinePlayer(monarchId);
        Assert.IsNotNull(offlinePlayer, "The seeded monarch must be reachable through PlayerManager's ordinary offline path.");

        Assert.IsNull(
            offlinePlayer.Allegiance,
            "Reproduces the defect precondition: PlayerManager.Initialize() never calls AllegianceManager.LoadPlayer, "
                + "so the old `offlinePlayer.Allegiance != null && ...` expression would always evaluate isMonarch=false here.");

        Assert.IsTrue(
            AllegianceManager.IsMonarch(offlinePlayer),
            "AllegianceManager.IsMonarch must derive monarch status live from persisted state (VAULT-005), not the unpopulated cache.");
    }

    [TestMethod]
    public async Task CheckMonarchDeletion_ForAMonarchNeverLoadedThisSession_WithANonemptyVault_BlocksDeletion()
    {
        var monarchId = await SeedMonarchNeverLoadedThisSessionAsync();

        var vaultOwnerId = CloudOwnerIdentity.ForAllegianceVault(ShardId, monarchId);
        var itemBiotaId = NextId();
        await InsertBiotaAsync(itemBiotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            var deposit = await boundary.DepositAsync(itemBiotaId, ShardId, vaultOwnerId, Guid.NewGuid());
            Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, deposit.Kind, deposit.Reason);
        }

        // Exactly the call sites in CharacterHandler.CharacterDelete / CharacterCommands.HandleCharacterForcedDelete:
        // derive isMonarch from the offline, DB-backed player, then ask the guard.
        var offlinePlayer = PlayerManager.GetOfflinePlayer(monarchId);
        Assert.IsNotNull(offlinePlayer);

        var isMonarch = AllegianceManager.IsMonarch(offlinePlayer);
        var decision = CloudIdentityEventManager.CheckMonarchDeletion(monarchId, isMonarch);

        Assert.IsFalse(
            decision.IsAllowed,
            "A monarch never loaded into AllegianceManager's cache this session must still have their nonempty-vault deletion blocked (VAULT-005).");
    }

    private static async Task InsertCharacterAsync(uint characterId, uint accountId, string name)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `character` (id, account_Id, name, is_Plussed, is_Deleted, delete_Time, last_Login_Timestamp, total_Logins)
            VALUES (@id, @accountId, @name, 0, 0, 0, 0, 0);
            """;
        command.Parameters.AddWithValue("@id", characterId);
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@name", name);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertBiotaAsync(uint biotaId)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota (id, weenie_Class_Id, weenie_Type, populated_Collection_Flags)
            VALUES (@id, 1, 1, 0);
            """;
        command.Parameters.AddWithValue("@id", biotaId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task SetNameAsync(uint biotaId, string name)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota_properties_string (object_Id, type, value)
            VALUES (@objectId, @type, @value);
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@type", NamePropertyType);
        command.Parameters.AddWithValue("@value", name);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task GrantMonarchAsync(uint characterId, uint monarchId)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota_properties_i_i_d (object_Id, type, value)
            VALUES (@objectId, @type, @value);
            """;
        command.Parameters.AddWithValue("@objectId", characterId);
        command.Parameters.AddWithValue("@type", MonarchPropertyType);
        command.Parameters.AddWithValue("@value", monarchId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task GrantPatronAsync(uint characterId, uint patronId)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota_properties_i_i_d (object_Id, type, value)
            VALUES (@objectId, @type, @value);
            """;
        command.Parameters.AddWithValue("@objectId", characterId);
        command.Parameters.AddWithValue("@type", PatronPropertyType);
        command.Parameters.AddWithValue("@value", patronId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds the persisted Allegiance biota <see cref="AllegianceManager.GetAllegiance"/> looks up via
    /// <c>ShardDatabase.GetAllegianceID</c> (a biota whose WeenieType is Allegiance and whose own
    /// Monarch instance property is the monarch's GUID) -- exactly what the real game persists the
    /// first time anyone swears allegiance, modeled directly rather than by driving a live swear
    /// through <c>Player.SwearAllegiance</c> (which needs a live, networked Session this fixture
    /// deliberately does not stand up).
    /// </summary>
    private static async Task InsertAllegianceBiotaAsync(uint allegianceBiotaId, uint monarchId)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota (id, weenie_Class_Id, weenie_Type, populated_Collection_Flags)
            VALUES (@id, 1, @weenieType, 0);
            """;
        command.Parameters.AddWithValue("@id", allegianceBiotaId);
        command.Parameters.AddWithValue("@weenieType", AllegianceWeenieType);
        await command.ExecuteNonQueryAsync();

        await GrantMonarchAsync(allegianceBiotaId, monarchId);
    }
}
