using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Minimal ace_shard seeding/assertion helpers for proving the Cloud custody exclusivity
/// invariants (ARCH-005) against real native biota rows, without depending on ACE.Database (Cloud
/// projects must not reference live ACE world objects).
/// </summary>
internal static class AceShardTestData
{
    private const short ContainerPropertyType = 2; // PropertyInstanceId.Container
    private const short WielderPropertyType = 3; // PropertyInstanceId.Wielder
    private const short MonarchPropertyType = 26; // PropertyInstanceId.Monarch
    private const short LocationPositionType = 1; // PositionType.Location
    private const short StackSizePropertyType = 12; // PropertyInt.StackSize
    private const short ValuePropertyType = 19; // PropertyInt.Value

    public static async Task InsertBiotaAsync(string aceShardConnectionString, uint biotaId)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota (id, weenie_Class_Id, weenie_Type, populated_Collection_Flags)
            VALUES (@id, 1, 1, 0);
            """;
        command.Parameters.AddWithValue("@id", biotaId);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<bool> BiotaExistsAsync(string aceShardConnectionString, uint biotaId)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM biota WHERE id = @biotaId;";
        command.Parameters.AddWithValue("@biotaId", biotaId);

        var count = (long)(await command.ExecuteScalarAsync())!;
        return count > 0;
    }

    /// <summary>
    /// Reads the native PropertyInt.StackSize row for a biota, or null if that biota has none.
    /// </summary>
    public static async Task<int?> GetStackSizeAsync(string aceShardConnectionString, uint biotaId)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM biota_properties_int WHERE object_Id = @objectId AND type = @type;";
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@type", StackSizePropertyType);

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    /// <summary>
    /// Sets a coin-stack biota's PropertyInt.Value (DEP-006), the total Pyreal amount
    /// <c>CloudCustodyBoundary.ReadBiotaCoinValueAsync</c> reads back when revalidating a raw Pyreal
    /// Remainder withdrawal's delivered biotas.
    /// </summary>
    public static async Task SetCoinValueAsync(string aceShardConnectionString, uint biotaId, long value)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota_properties_int (object_Id, type, value)
            VALUES (@objectId, @type, @value)
            ON DUPLICATE KEY UPDATE value = @value;
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@type", ValuePropertyType);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds one native runtime-enchantment registry row (DEP-005) so a test can deposit an item
    /// whose ace_shard row's <c>start_Time</c> deliberately disagrees with the live remaining
    /// duration a Cloud Custodian deposit captures -- modeling ACE's periodic autosave lag between
    /// a live <c>WorldObject</c>'s in-memory <c>EnchantmentManager</c> and its last-persisted
    /// ace_shard row (`Player.BuildRuntimeEnchantments`'s doc comment).
    /// </summary>
    public static async Task InsertEnchantmentRegistryRowAsync(
        string aceShardConnectionString, uint biotaId, int spellId, double startTime, double duration, ushort layerId = 0, uint casterObjectId = 0)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota_properties_enchantment_registry
                (object_Id, enchantment_Category, spell_Id, layer_Id, has_Spell_Set_Id, spell_Category, power_Level,
                 start_Time, duration, caster_Object_Id, degrade_Modifier, degrade_Limit, last_Time_Degraded,
                 stat_Mod_Type, stat_Mod_Key, stat_Mod_Value, spell_Set_Id)
            VALUES
                (@objectId, 0, @spellId, @layerId, 0, 0, 0, @startTime, @duration, @casterObjectId, 0, 0, 0, 0, 0, 0, 0);
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@spellId", spellId);
        command.Parameters.AddWithValue("@layerId", layerId);
        command.Parameters.AddWithValue("@startTime", startTime);
        command.Parameters.AddWithValue("@duration", duration);
        command.Parameters.AddWithValue("@casterObjectId", casterObjectId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Reads a native runtime-enchantment registry row's persisted <c>start_Time</c>, or null if no
    /// row matches -- what a test asserts against to prove a Frozen Enchantment neither ticks during
    /// Cloud custody nor resumes from a stale value at withdrawal (DEP-005). Keys on <paramref
    /// name="layerId"/> as well as <paramref name="spellId"/> (defaulting to 0, this file's existing
    /// single-layer convention) because <c>biota_properties_enchantment_registry</c>'s real identity
    /// is (object_Id, spell_Id, layer_Id) -- multiple layers of the same spell are a supported case a
    /// test must be able to tell apart (issue #15 review).
    /// </summary>
    public static async Task<double?> GetEnchantmentStartTimeAsync(string aceShardConnectionString, uint biotaId, int spellId, ushort layerId = 0)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT start_Time FROM biota_properties_enchantment_registry
            WHERE object_Id = @objectId AND spell_Id = @spellId AND layer_Id = @layerId;
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@spellId", spellId);
        command.Parameters.AddWithValue("@layerId", layerId);

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? null : Convert.ToDouble(result);
    }

    /// <summary>
    /// Counts a biota's native runtime-enchantment registry rows for a given spell and layer, used to
    /// prove a Frozen Enchantment's ace_shard row is never removed while its biota is in Cloud
    /// custody.
    /// </summary>
    public static async Task<long> CountEnchantmentRegistryRowsAsync(string aceShardConnectionString, uint biotaId, int spellId, ushort layerId = 0)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM biota_properties_enchantment_registry
            WHERE object_Id = @objectId AND spell_Id = @spellId AND layer_Id = @layerId;
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@spellId", spellId);
        command.Parameters.AddWithValue("@layerId", layerId);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// Seeds one native ace_shard.character row (issue #17), the minimal columns
    /// <c>CloudCustodyBoundary.CharacterExistsAndIsNotDeletedAsync</c> and AUTH-003's Display
    /// Character projection care about.
    /// </summary>
    public static async Task InsertCharacterAsync(
        string aceShardConnectionString, uint characterId, uint accountId, string name, int totalLogins = 0, bool isDeleted = false)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO `character` (id, account_Id, name, is_Plussed, is_Deleted, delete_Time, last_Login_Timestamp, total_Logins)
            VALUES (@id, @accountId, @name, 0, @isDeleted, 0, 0, @totalLogins);
            """;
        command.Parameters.AddWithValue("@id", characterId);
        command.Parameters.AddWithValue("@accountId", accountId);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@isDeleted", isDeleted);
        command.Parameters.AddWithValue("@totalLogins", totalLogins);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Physically removes a native ace_shard.character row, modeling an out-of-band deletion that
    /// bypasses ACE's own guarded deletion path entirely (VAULT-005's recovery scenario) rather than
    /// the ordinary soft-delete (<c>is_Deleted</c>/<c>delete_Time</c>) flow.
    /// </summary>
    public static async Task DeleteCharacterRowAsync(string aceShardConnectionString, uint characterId)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM `character` WHERE id = @id;";
        command.Parameters.AddWithValue("@id", characterId);
        await command.ExecuteNonQueryAsync();
    }

    public static Task GrantContainerAsync(string aceShardConnectionString, uint biotaId, uint containerId) =>
        InsertIidPropertyAsync(aceShardConnectionString, biotaId, ContainerPropertyType, containerId);

    public static Task GrantWielderAsync(string aceShardConnectionString, uint biotaId, uint wielderId) =>
        InsertIidPropertyAsync(aceShardConnectionString, biotaId, WielderPropertyType, wielderId);

    /// <summary>
    /// Sets a character's persisted Monarch instance property (issue #17), modeling what
    /// <c>Player.SwearAllegiance</c> persists when a character swears allegiance to someone else --
    /// including a former monarch swearing into another allegiance (VAULT-004's trigger for Vault
    /// Absorption).
    /// </summary>
    public static Task GrantMonarchAsync(string aceShardConnectionString, uint characterId, uint monarchId) =>
        InsertIidPropertyAsync(aceShardConnectionString, characterId, MonarchPropertyType, monarchId);

    public static async Task GrantLocationAsync(string aceShardConnectionString, uint biotaId)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota_properties_position
                (object_Id, position_Type, obj_Cell_Id, origin_X, origin_Y, origin_Z, angles_W, angles_X, angles_Y, angles_Z)
            VALUES
                (@objectId, @positionType, 1, 0, 0, 0, 1, 0, 0, 0);
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@positionType", LocationPositionType);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<bool> HasContainerAsync(string aceShardConnectionString, uint biotaId)
    {
        var count = await CountContainerRowsAsync(aceShardConnectionString, biotaId);
        return count > 0;
    }

    public static async Task<bool> HasWielderAsync(string aceShardConnectionString, uint biotaId)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM biota_properties_i_i_d WHERE object_Id = @objectId AND type = @type;
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@type", WielderPropertyType);

        var count = (long)(await command.ExecuteScalarAsync())!;
        return count > 0;
    }

    public static async Task<bool> HasSpecificContainerAsync(string aceShardConnectionString, uint biotaId, uint containerId)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM biota_properties_i_i_d
            WHERE object_Id = @objectId AND type = @type AND value = @containerId;
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@type", ContainerPropertyType);
        command.Parameters.AddWithValue("@containerId", containerId);

        var count = (long)(await command.ExecuteScalarAsync())!;
        return count > 0;
    }

    public static async Task<long> CountContainerRowsAsync(string aceShardConnectionString, uint biotaId)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM biota_properties_i_i_d WHERE object_Id = @objectId AND type = @type;
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@type", ContainerPropertyType);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task InsertIidPropertyAsync(
        string aceShardConnectionString, uint objectId, short propertyType, uint value)
    {
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota_properties_i_i_d (object_Id, type, value)
            VALUES (@objectId, @type, @value);
            """;
        command.Parameters.AddWithValue("@objectId", objectId);
        command.Parameters.AddWithValue("@type", propertyType);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync();
    }
}
