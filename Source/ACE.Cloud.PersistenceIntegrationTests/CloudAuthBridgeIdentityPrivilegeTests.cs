using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green test for issue #18's Red section: "integration tests proving Cloud service
/// credentials can transact the dedicated schema but cannot update auth password fields or native
/// biota tables" -- the Auth Bridge half specifically. Unlike
/// <see cref="CloudCompanionPrivilegeTests"/> (which proves the Backend/Worker identity has zero
/// <c>ace_auth</c> access at all), this provisions the distinct, narrowly-privileged read-only
/// <c>ace_auth.account</c> identity <c>AuthBridgeOptions.AceAuthConnectionString</c>'s doc comment
/// requires (AUTH-002) and proves, against a real MariaDB grant, that it can read the password
/// fields the bridge's verifier needs but is refused the moment it tries to write them or reach any
/// other schema.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudAuthBridgeIdentityPrivilegeTests
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

    [TestMethod]
    public async Task AuthBridgeIdentity_CanSelectPasswordHashAndSaltFromAceAuthAccountTable()
    {
        var username = "cloud_auth_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var authBridgeConnectionString = await _fixture.CreateRestrictedAuthBridgeConnectionStringAsync(username, password);

        var accountId = await AceAuthTestData.InsertAccountAsync(_fixture.AceAuthConnectionString);

        await using var authBridgeConnection = new MySqlConnection(authBridgeConnectionString);
        await authBridgeConnection.OpenAsync();

        await using var readPasswordFields = authBridgeConnection.CreateCommand();
        readPasswordFields.CommandText = "SELECT passwordHash, passwordSalt FROM account WHERE accountId = @accountId;";
        readPasswordFields.Parameters.AddWithValue("@accountId", accountId);

        await using var reader = await readPasswordFields.ExecuteReaderAsync();
        var hasRow = await reader.ReadAsync();

        Assert.IsTrue(hasRow,
            "AUTH-002: the Auth Bridge's own restricted identity must actually be able to read the password fields it needs to reuse ACE's verifier.");
    }

    [TestMethod]
    public async Task AuthBridgeIdentity_CannotUpdateAceAuthAccountPasswordFields()
    {
        var username = "cloud_auth_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var authBridgeConnectionString = await _fixture.CreateRestrictedAuthBridgeConnectionStringAsync(username, password);

        var accountId = await AceAuthTestData.InsertAccountAsync(_fixture.AceAuthConnectionString);

        await using var authBridgeConnection = new MySqlConnection(authBridgeConnectionString);
        await authBridgeConnection.OpenAsync();

        await using var writePasswordField = authBridgeConnection.CreateCommand();
        writePasswordField.CommandText = "UPDATE account SET passwordHash = @hash WHERE accountId = @accountId;";
        writePasswordField.Parameters.AddWithValue("@hash", "attacker-controlled-hash");
        writePasswordField.Parameters.AddWithValue("@accountId", accountId);

        var deniedAccess = await Assert.ThrowsExactlyAsync<MySqlException>(() => writePasswordField.ExecuteNonQueryAsync());

        Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedAccess.ErrorCode,
            "AUTH-002: the Auth Bridge's own read-only identity must be refused by MariaDB itself when it tries to write ace_auth.account, "
                + "not merely by application-level convention -- the Cloud backend never stores or mutates passwords.");
    }

    [TestMethod]
    public async Task AuthBridgeIdentity_CannotEvenSelectFromCloudOrShardSchemas()
    {
        var username = "cloud_auth_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var authBridgeConnectionString = await _fixture.CreateRestrictedAuthBridgeConnectionStringAsync(username, password);

        await using var authBridgeConnection = new MySqlConnection(authBridgeConnectionString);
        await authBridgeConnection.OpenAsync();

        await using (var readCloudSchema = authBridgeConnection.CreateCommand())
        {
            readCloudSchema.CommandText = "SELECT COUNT(*) FROM ace_cloud.CloudShardBinding;";
            var deniedCloudAccess = await Assert.ThrowsExactlyAsync<MySqlException>(() => readCloudSchema.ExecuteScalarAsync());
            Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedCloudAccess.ErrorCode,
                "ARCH-004: the Auth Bridge's identity is scoped to ace_auth.account only and must have no access to the Cloud schema.");
        }

        await using (var readShardSchema = authBridgeConnection.CreateCommand())
        {
            readShardSchema.CommandText = "SELECT COUNT(*) FROM ace_shard.biota;";
            var deniedShardAccess = await Assert.ThrowsExactlyAsync<MySqlException>(() => readShardSchema.ExecuteScalarAsync());
            Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedShardAccess.ErrorCode,
                "ARCH-004: the Auth Bridge's identity is scoped to ace_auth.account only and must have no access to native ace_shard tables.");
        }
    }
}
