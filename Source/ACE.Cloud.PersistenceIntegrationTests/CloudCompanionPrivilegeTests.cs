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

    /// <summary>
    /// Issue #39 narrowed this from a blanket "denied everywhere in ace_shard" assertion: the
    /// companion identity now holds two narrow, explicit SELECT grants (see
    /// <see cref="CompanionIdentity_CanSelectFromTheTwoTablesCollaborationReadersNeed_ButNothingElseInAceShard"/>),
    /// but every other ace_shard table -- including <c>biota</c>, the native custody surface ARCH-002
    /// reserves exclusively to ACE -- remains denied by MariaDB itself.
    /// </summary>
    [TestMethod]
    public async Task CompanionIdentity_CannotSelectFromUngrantedAceShardTables()
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
    /// PR #157 blocking human-acceptance feedback #2 (issue #39): the local acceptance migrator's
    /// companion identity previously had no ace_shard access at all, so
    /// <c>CloudSharingGrantGateway.TryResolveCurrentCharacterAccountAsync</c> and the live allegiance
    /// readers (<see cref="ACE.Cloud.Persistence.CloudAllegianceVaultTransactionGateway"/>,
    /// <see cref="ACE.Cloud.Persistence.CloudAllegianceVaultGateway"/>) failed with an unhandled MySQL
    /// access-denied error. This proves the fix grants exactly -- not more than -- the two tables those
    /// readers use, and that the grant is read-only: writes to either table remain refused.
    /// </summary>
    [TestMethod]
    public async Task CompanionIdentity_CanSelectFromTheTwoTablesCollaborationReadersNeed_ButNothingElseInAceShard()
    {
        var username = "cloud_web_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var companionConnectionString = await _fixture.CreateRestrictedCompanionConnectionStringAsync(username, password);

        await using var companionConnection = new MySqlConnection(companionConnectionString);
        await companionConnection.OpenAsync();

        await using (var readCharacter = companionConnection.CreateCommand())
        {
            readCharacter.CommandText = "SELECT COUNT(*) FROM ace_shard.character;";
            await readCharacter.ExecuteScalarAsync();
        }

        await using (var readMonarchProperty = companionConnection.CreateCommand())
        {
            readMonarchProperty.CommandText = "SELECT COUNT(*) FROM ace_shard.biota_properties_i_i_d;";
            await readMonarchProperty.ExecuteScalarAsync();
        }

        await using (var readUngrantedTable = companionConnection.CreateCommand())
        {
            readUngrantedTable.CommandText = "SELECT COUNT(*) FROM ace_shard.biota_properties_position;";
            var deniedRead = await Assert.ThrowsExactlyAsync<MySqlException>(() => readUngrantedTable.ExecuteScalarAsync());
            Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedRead.ErrorCode,
                "Issue #39: the fix must not widen into a broad ace_shard.* grant.");
        }

        var biotaId = 1_000_101u;
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var writeCharacterTable = companionConnection.CreateCommand())
        {
            writeCharacterTable.CommandText = "UPDATE ace_shard.character SET name = 'Hijacked' WHERE id = @id;";
            writeCharacterTable.Parameters.AddWithValue("@id", biotaId);
            var deniedWrite = await Assert.ThrowsExactlyAsync<MySqlException>(() => writeCharacterTable.ExecuteNonQueryAsync());
            Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedWrite.ErrorCode,
                "ARCH-004: SELECT on ace_shard.character must not imply write access.");
        }

        await using (var writeMonarchProperty = companionConnection.CreateCommand())
        {
            writeMonarchProperty.CommandText =
                "INSERT INTO ace_shard.biota_properties_i_i_d (object_Id, type, value) VALUES (@objectId, 26, @monarchId);";
            writeMonarchProperty.Parameters.AddWithValue("@objectId", biotaId);
            writeMonarchProperty.Parameters.AddWithValue("@monarchId", biotaId);
            var deniedWrite = await Assert.ThrowsExactlyAsync<MySqlException>(() => writeMonarchProperty.ExecuteNonQueryAsync());
            Assert.AreEqual(MySqlErrorCode.TableAccessDenied, deniedWrite.ErrorCode,
                "ARCH-004: SELECT on ace_shard.biota_properties_i_i_d must not imply write access.");
        }
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
