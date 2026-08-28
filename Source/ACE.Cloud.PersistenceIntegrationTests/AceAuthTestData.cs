using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Minimal ace_auth seeding helper for proving ARCH-004's "cannot update auth password fields"
/// invariant against a real native account row, without depending on ACE.Database (Cloud projects
/// must not reference live ACE world objects or native-biota mutation repositories).
/// </summary>
internal static class AceAuthTestData
{
    public static async Task<uint> InsertAccountAsync(string aceAuthConnectionString)
    {
        await using var connection = new MySqlConnection(aceAuthConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO account (accountName, passwordHash, passwordSalt, accessLevel)
            VALUES (@accountName, @passwordHash, 'use bcrypt', 0);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@accountName", "cloud-test-" + Guid.NewGuid().ToString("N")[..12]);
        command.Parameters.AddWithValue("@passwordHash", Guid.NewGuid().ToString("N"));

        return Convert.ToUInt32(await command.ExecuteScalarAsync());
    }
}
