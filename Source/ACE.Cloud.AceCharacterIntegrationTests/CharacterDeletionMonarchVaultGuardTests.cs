using System.Net;
using System.Reflection;
using System.Threading;
using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using ACE.Cloud.PersistenceIntegrationTests;
using ACE.Common;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Server.Entity;
using ACE.Server.Managers;
using ACE.Server.Network;
using ACE.Server.Network.Managers;
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

        // Session.CheckCharactersForDeletion (issue #17 review, finding 1) constructs a real Session,
        // whose NetworkSession constructor calls SocketManager.GetMatchedConnectionListener. That
        // reads SocketManager's private static `listeners` array, which only SocketManager.Initialize
        // (a real socket bind, never invoked by this disposable fixture) ever populates; without this,
        // it is null and the lookup throws a NullReferenceException. An empty array reproduces exactly
        // what a real deployment sees for an unmatched connection (GetMatchedConnectionListener finding
        // no match returns null) without binding any real socket.
        var socketManagerListenersField = typeof(SocketManager).GetField("listeners", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("SocketManager.listeners was not found.");
        if (socketManagerListenersField.GetValue(null) == null)
        {
            socketManagerListenersField.SetValue(null, Array.Empty<ConnectionListener>());
        }

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

    /// <summary>
    /// Red -&gt; Green regression test for issue #17's review, finding 1 (P1): VAULT-005's guard is
    /// re-checked at self-service delete *request* time (<see cref="CharacterHandler.CharacterDelete"/>),
    /// but the actual, irreversible finalization that happens later --
    /// <see cref="Session.CheckCharactersForDeletion"/>, called on every subsequent character-list
    /// refresh once <see cref="Character.DeleteTime"/> has elapsed -- never re-ran the guard. A vault
    /// could be contributed to by any other current allegiance member during the (default one hour)
    /// restore window, so a monarch whose vault was empty at request time could still have their
    /// character irreversibly deleted with a nonempty vault. This reproduces that precondition
    /// directly: a monarch character already past its DeleteTime, with a nonempty vault, handed to
    /// <see cref="Session.CheckCharactersForDeletion"/> exactly as the login character-list refresh
    /// path does.
    /// </summary>
    [TestMethod]
    public async Task CheckCharactersForDeletion_ForAMonarchPastDeleteTime_WithANonemptyVault_DoesNotFinalizeTheDeletion()
    {
        // SeedMonarchNeverLoadedThisSessionAsync always seeds its monarch character under
        // ace_shard.character.account_Id = 1; a real account row is required here (unlike the other
        // tests in this class) because this test drives the finalization path all the way through
        // PlayerManager.ProcessDeletedPlayer on an unguarded run, which resolves the character's
        // ace_auth account. INSERT IGNORE keeps this idempotent across every test in this class that
        // reuses the same fixed account ID.
        await InsertAccountIfNotExistsAsync(accountId: 1, accountName: "old-monarch-account");

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

        // The same provisional state CharacterHandler.CharacterDelete leaves behind: DeleteTime set in
        // the past (the restore window has already elapsed) but IsDeleted still false.
        var character = new Character
        {
            Id = monarchId,
            AccountId = 1,
            Name = "OldMonarch",
            IsDeleted = false,
            DeleteTime = (ulong)(Time.GetUnixTime() - 10),
        };

        var session = new Session(new ConnectionListener(IPAddress.Loopback, 0), new IPEndPoint(IPAddress.Loopback, 0), clientId: 1, serverId: 1);
        session.Characters.Add(character);

        session.CheckCharactersForDeletion();

        Assert.IsFalse(
            character.IsDeleted,
            "A monarch's deletion must not be finalized while their Allegiance Vault is still nonempty (VAULT-005), even once "
                + "the restore window has elapsed.");

        Assert.Contains(
            character,
            session.Characters,
            "A blocked finalization must leave the character in the session's pending-deletion list, not silently drop it.");

        Assert.IsNotNull(
            PlayerManager.GetOfflinePlayer(monarchId),
            "PlayerManager must not have processed this character as deleted while its vault deletion was blocked.");

        await using var verifyContext = new CloudDbContext(CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString));
        var vaultGateway = new CloudAllegianceVaultGateway(verifyContext);
        Assert.IsFalse(
            await vaultGateway.GetIsEmptyAsync(ShardId, monarchId),
            "The Allegiance Vault's contents must survive a blocked finalization.");
    }

    /// <summary>
    /// Seeds a minimal ace_auth.account row so <see cref="OfflinePlayer.Account"/> resolves (its
    /// constructor calls <c>DatabaseManager.Authentication.GetAccountById</c>), which
    /// <see cref="PlayerManager.ProcessDeletedPlayer"/> requires. Idempotent, since this file's
    /// character-seeding helper always uses the same fixed account IDs across every test in this class.
    /// </summary>
    private static async Task InsertAccountIfNotExistsAsync(uint accountId, string accountName)
    {
        var authConnectionString = new MySqlConnectionStringBuilder(_fixture.AceShardConnectionString) { Database = "ace_auth" }.ConnectionString;

        await using var connection = new MySqlConnection(authConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT IGNORE INTO account (accountId, accountName, passwordHash, accessLevel)
            VALUES (@accountId, @accountName, 'test', 0);
            """;
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@accountName", accountName);
        await command.ExecuteNonQueryAsync();
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

        await SetPopulatedCollectionFlagAsync(biotaId, ShardDatabase.PopulatedCollectionFlags.BiotaPropertiesString);
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

        await SetPopulatedCollectionFlagAsync(characterId, ShardDatabase.PopulatedCollectionFlags.BiotaPropertiesIID);
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

        await SetPopulatedCollectionFlagAsync(characterId, ShardDatabase.PopulatedCollectionFlags.BiotaPropertiesIID);
    }

    /// <summary>
    /// Production characters have this bitmask maintained by <see cref="ShardDatabase.SetBiotaPopulatedCollections"/>
    /// whenever a real save happens; ShardDatabase.GetBiota gates which property collections it loads on it (see
    /// its own doc comment for why), so this raw-SQL seeding must set the matching bit for every property table it
    /// inserts into, or PlayerManager.Initialize() will load the seeded character back with those properties missing.
    /// </summary>
    private static async Task SetPopulatedCollectionFlagAsync(uint biotaId, ShardDatabase.PopulatedCollectionFlags flag)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE biota SET populated_Collection_Flags = populated_Collection_Flags | @flag WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", biotaId);
        command.Parameters.AddWithValue("@flag", (uint)flag);
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
