using ACE.Cloud.Domain;

using MySqlConnector;

namespace ACE.Cloud.AuthBridge;

/// <summary>
/// Reads <c>ace_auth.account</c> directly with plain ADO.NET (AUTH-002), against
/// <see cref="AuthBridgeOptions.AceAuthConnectionString"/>'s narrowly privileged, read-only
/// identity -- <c>GRANT SELECT ON ace_auth.account</c>, proven by
/// <c>ACE.Cloud.PersistenceIntegrationTests.CloudAuthBridgeIdentityPrivilegeTests</c>. Deliberately
/// not ACE.Database's own <c>AuthDbContext</c>/<c>Account</c> repository: that type is
/// read/write-capable and this bridge must never even attempt the write half of
/// <c>AccountExtensions.PasswordMatches</c>'s bcrypt-migration behavior (its identity cannot
/// perform it regardless, but a bridge that never even tries stays true to "the Cloud backend never
/// stores passwords, logs them, or implements password-hash verification").
/// </summary>
public sealed class AceAuthAccountReader : IAceAuthAccountReader
{
    private const string SelectColumns =
        "accountId, accountName, passwordHash, passwordSalt, accessLevel, ban_Expire_Time, ban_Reason";

    private readonly string _connectionString;

    public AceAuthAccountReader(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("A connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<CloudAceAccountSnapshot?> FindByAccountNameAsync(string accountName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new ArgumentException("An account name is required.", nameof(accountName));
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM account WHERE accountName = @accountName;";
        command.Parameters.AddWithValue("@accountName", accountName);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSnapshot(reader) : null;
    }

    public async Task<CloudAceAccountSnapshot?> FindByAccountIdAsync(uint accountId, CancellationToken cancellationToken = default)
    {
        if (accountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(accountId), "A real ACE account ID is required.");
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM account WHERE accountId = @accountId;";
        command.Parameters.AddWithValue("@accountId", accountId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSnapshot(reader) : null;
    }

    private static CloudAceAccountSnapshot ReadSnapshot(MySqlDataReader reader) => new(
        AccountId: reader.GetUInt32(0),
        AccountName: reader.GetString(1),
        PasswordHash: reader.GetString(2),
        PasswordSalt: reader.GetString(3),
        AccessLevel: reader.GetUInt32(4),
        BanExpireTime: reader.IsDBNull(5) ? null : reader.GetDateTime(5),
        BanReason: reader.IsDBNull(6) ? null : reader.GetString(6));
}
