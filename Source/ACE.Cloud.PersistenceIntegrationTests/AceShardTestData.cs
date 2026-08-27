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
    private const short LocationPositionType = 1; // PositionType.Location

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

    public static Task GrantContainerAsync(string aceShardConnectionString, uint biotaId, uint containerId) =>
        InsertIidPropertyAsync(aceShardConnectionString, biotaId, ContainerPropertyType, containerId);

    public static Task GrantWielderAsync(string aceShardConnectionString, uint biotaId, uint wielderId) =>
        InsertIidPropertyAsync(aceShardConnectionString, biotaId, WielderPropertyType, wielderId);

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
        await using var connection = new MySqlConnection(aceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM biota_properties_i_i_d WHERE object_Id = @objectId AND type = @type;
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@type", ContainerPropertyType);

        var count = (long)(await command.ExecuteScalarAsync())!;
        return count > 0;
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
