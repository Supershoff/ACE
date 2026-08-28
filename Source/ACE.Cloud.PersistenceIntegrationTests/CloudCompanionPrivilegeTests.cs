using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green test for issue #11's acceptance criterion "Privilege tests prove the authority
/// split rather than merely documenting it" and its Red section: "Test that the companion
/// credential cannot update native ACE biota tables" (ARCH-004). Provisions a real MariaDB
/// identity granted only on ace_cloud (the shape a future Operator Bootstrap issue will create for
/// the companion web service) and proves, by actually attempting the writes, that it can transact
/// the Cloud schema but is refused by the database itself -- not just by application code -- the
/// moment it touches a native ace_shard table.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCompanionPrivilegeTests
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
    public async Task CompanionIdentity_CannotWriteNativeAceShardTables_EvenThoughItCanWriteTheCloudSchema()
    {
        var username = "cloud_web_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var companionConnectionString = await _fixture.CreateRestrictedCompanionConnectionStringAsync(username, password);

        await using var companionConnection = new MySqlConnection(companionConnectionString);
        await companionConnection.OpenAsync();

        // Sanity check: the restricted identity is not simply broken -- it can transact its own
        // schema, matching CONTEXT.md's "narrowly privileged database identity that can transact
        // the Cloud schema."
        await using (var readCloudSchema = companionConnection.CreateCommand())
        {
            readCloudSchema.CommandText = "SELECT COUNT(*) FROM CloudShardBinding;";
            await readCloudSchema.ExecuteScalarAsync();
        }

        var biotaId = 1_000_001u;
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var writeAceShard = companionConnection.CreateCommand();
        writeAceShard.CommandText = """
            INSERT INTO ace_shard.biota_properties_i_i_d (object_Id, type, value)
            VALUES (@objectId, 2, @containerId);
            """;
        writeAceShard.Parameters.AddWithValue("@objectId", biotaId);
        writeAceShard.Parameters.AddWithValue("@containerId", 1_000_002u);

        var deniedAccess = await Assert.ThrowsExactlyAsync<MySqlException>(() => writeAceShard.ExecuteNonQueryAsync());

        Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedAccess.ErrorCode,
            "ARCH-004: the companion database identity must be refused by MariaDB itself when it touches a native ace_shard table, not merely by application-level convention.");
    }

    [TestMethod]
    public async Task CompanionIdentity_CannotEvenSelectFromNativeAceShardTables()
    {
        var username = "cloud_web_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var companionConnectionString = await _fixture.CreateRestrictedCompanionConnectionStringAsync(username, password);

        await using var companionConnection = new MySqlConnection(companionConnectionString);
        await companionConnection.OpenAsync();

        await using var readAceShard = companionConnection.CreateCommand();
        readAceShard.CommandText = "SELECT COUNT(*) FROM ace_shard.biota;";

        var deniedAccess = await Assert.ThrowsExactlyAsync<MySqlException>(() => readAceShard.ExecuteScalarAsync());

        Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedAccess.ErrorCode);
    }

    /// <summary>
    /// Red -> Green test for issue #18's Red section: "integration tests proving Cloud service
    /// credentials can transact the dedicated schema but cannot update auth password fields or
    /// native biota tables" -- the "auth password fields" half, which
    /// <see cref="CompanionIdentity_CannotWriteNativeAceShardTables_EvenThoughItCanWriteTheCloudSchema"/>
    /// never covered.
    /// </summary>
    [TestMethod]
    public async Task CompanionIdentity_CannotUpdateAceAuthPasswordFields()
    {
        var username = "cloud_web_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var companionConnectionString = await _fixture.CreateRestrictedCompanionConnectionStringAsync(username, password);

        var accountId = await AceAuthTestData.InsertAccountAsync(_fixture.AceAuthConnectionString);

        await using var companionConnection = new MySqlConnection(companionConnectionString);
        await companionConnection.OpenAsync();

        await using var writeAceAuth = companionConnection.CreateCommand();
        writeAceAuth.CommandText = "UPDATE ace_auth.account SET passwordHash = @hash WHERE accountId = @accountId;";
        writeAceAuth.Parameters.AddWithValue("@hash", "attacker-controlled-hash");
        writeAceAuth.Parameters.AddWithValue("@accountId", accountId);

        var deniedAccess = await Assert.ThrowsExactlyAsync<MySqlException>(() => writeAceAuth.ExecuteNonQueryAsync());

        Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedAccess.ErrorCode,
            "ARCH-004: the companion database identity must be refused by MariaDB itself when it touches ace_auth.account, "
                + "not merely by application-level convention -- AUTH-002 requires the Cloud backend to never write passwords.");
    }

    [TestMethod]
    public async Task CompanionIdentity_CannotEvenSelectFromAceAuthAccountTable()
    {
        var username = "cloud_web_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var companionConnectionString = await _fixture.CreateRestrictedCompanionConnectionStringAsync(username, password);

        await using var companionConnection = new MySqlConnection(companionConnectionString);
        await companionConnection.OpenAsync();

        await using var readAceAuth = companionConnection.CreateCommand();
        readAceAuth.CommandText = "SELECT COUNT(*) FROM ace_auth.account;";

        var deniedAccess = await Assert.ThrowsExactlyAsync<MySqlException>(() => readAceAuth.ExecuteScalarAsync());

        Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedAccess.ErrorCode);
    }
}
