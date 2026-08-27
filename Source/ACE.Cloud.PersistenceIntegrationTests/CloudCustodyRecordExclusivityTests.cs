using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #2: the first-class Cloud Custody Record schema and the database
/// invariants that keep world possession and Cloud custody exclusive (ARCH-004, ARCH-005,
/// INV-001). Every invalid state listed in the issue's Red section gets its own test proving the
/// database itself rejects it, not just application code.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodyRecordExclusivityTests
{
    private const string BoundShardId = "us1";
    private const string UnboundShardId = "us2";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 90_000;

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

    [TestInitialize]
    public async Task TestInitialize()
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using (var deleteCustody = connection.CreateCommand())
        {
            deleteCustody.CommandText = "DELETE FROM CloudCustodyRecord;";
            await deleteCustody.ExecuteNonQueryAsync();
        }

        await using (var deleteBinding = connection.CreateCommand())
        {
            deleteBinding.CommandText = "DELETE FROM CloudShardBinding;";
            await deleteBinding.ExecuteNonQueryAsync();
        }

        await using var insertBinding = connection.CreateCommand();
        insertBinding.CommandText = """
            INSERT INTO CloudShardBinding (Id, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion)
            VALUES (1, @shardId, '0.1.0', '0.1.0', '0.1.0');
            """;
        insertBinding.Parameters.AddWithValue("@shardId", BoundShardId);
        await insertBinding.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task DuplicateCustody_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await InsertCustodyRecordAsync(connection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid());

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertCustodyRecordAsync(connection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid()));

        StringAssert.Contains(exception.Message, "BiotaId");
    }

    [TestMethod]
    public async Task CrossShardOwnership_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertCustodyRecordAsync(connection, Guid.NewGuid(), biotaId, UnboundShardId, Guid.NewGuid()));

        StringAssert.Contains(exception.Message, "FK_CloudCustodyRecord_CloudShardBinding_ShardId");
    }

    [TestMethod]
    public async Task MissingNativeBiotaReference_IsRejected()
    {
        var biotaId = NextBiotaId(); // never inserted into ace_shard.biota

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertCustodyRecordAsync(connection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid()));

        StringAssert.Contains(exception.Message, "does not exist in ace_shard");
    }

    [TestMethod]
    public async Task SimultaneousCustody_WithContainerPossession_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.GrantContainerAsync(_fixture.AceShardConnectionString, biotaId, containerId: NextBiotaId());

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertCustodyRecordAsync(connection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid()));

        StringAssert.Contains(exception.Message, "world possession");
    }

    [TestMethod]
    public async Task SimultaneousCustody_WithWielderPossession_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.GrantWielderAsync(_fixture.AceShardConnectionString, biotaId, wielderId: NextBiotaId());

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertCustodyRecordAsync(connection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid()));

        StringAssert.Contains(exception.Message, "world possession");
    }

    [TestMethod]
    public async Task SimultaneousCustody_WithLocationPossession_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.GrantLocationAsync(_fixture.AceShardConnectionString, biotaId);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertCustodyRecordAsync(connection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid()));

        StringAssert.Contains(exception.Message, "world possession");
    }

    [TestMethod]
    public async Task WorldSide_CannotGrantContainer_WhenBiotaIsCloudCustodied()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var cloudConnection = new MySqlConnection(_fixture.CloudConnectionString))
        {
            await cloudConnection.OpenAsync();
            await InsertCustodyRecordAsync(cloudConnection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid());
        }

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => AceShardTestData.GrantContainerAsync(_fixture.AceShardConnectionString, biotaId, containerId: NextBiotaId()));

        StringAssert.Contains(exception.Message, "Cloud custody");
    }

    [TestMethod]
    public async Task WorldSide_CannotGrantLocation_WhenBiotaIsCloudCustodied()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using (var cloudConnection = new MySqlConnection(_fixture.CloudConnectionString))
        {
            await cloudConnection.OpenAsync();
            await InsertCustodyRecordAsync(cloudConnection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid());
        }

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => AceShardTestData.GrantLocationAsync(_fixture.AceShardConnectionString, biotaId));

        StringAssert.Contains(exception.Message, "Cloud custody");
    }

    [TestMethod]
    public async Task FailedCustodyTransition_PreservesOriginalWorldPossession()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.GrantContainerAsync(_fixture.AceShardConnectionString, biotaId, containerId: NextBiotaId());

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertCustodyRecordAsync(connection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid(), transaction));

        await transaction.RollbackAsync();

        // World possession is untouched: the failed transition never happened.
        Assert.IsTrue(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));

        await using var verifyConnection = new MySqlConnection(_fixture.CloudConnectionString);
        await verifyConnection.OpenAsync();
        await using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "SELECT COUNT(*) FROM CloudCustodyRecord WHERE BiotaId = @biotaId;";
        verifyCommand.Parameters.AddWithValue("@biotaId", biotaId);
        var count = (long)(await verifyCommand.ExecuteScalarAsync())!;
        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task NonStackCustody_HasExactlyOneOwnerAndOneNativeBiota()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        var ownerId = Guid.NewGuid();

        await using (var writeContext = new CloudDbContext(options))
        {
            writeContext.CloudCustodyRecords.Add(
                new CloudCustodyRecord(biotaId, BoundShardId, ownerId, Guid.NewGuid()));
            await writeContext.SaveChangesAsync();
        }

        await using (var readContext = new CloudDbContext(options))
        {
            var record = await readContext.CloudCustodyRecords.SingleAsync(r => r.BiotaId == biotaId);
            Assert.AreEqual(ownerId, record.OwnerId, "Non-stack custody must have exactly one owner.");
        }

        // Exactly one native biota: a second custody record can never claim the same biota.
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();
        var duplicateException = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertCustodyRecordAsync(connection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid()));
        StringAssert.Contains(duplicateException.Message, "BiotaId");

        // Exactly one owner: OwnerId cannot be omitted.
        var missingOwnerBiotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, missingOwnerBiotaId);

        await using var missingOwnerCommand = connection.CreateCommand();
        missingOwnerCommand.CommandText = """
            INSERT INTO CloudCustodyRecord (Id, BiotaId, ShardId, OwnerId, LedgerCorrelationId, Version)
            VALUES (@id, @biotaId, @shardId, NULL, @ledgerCorrelationId, 1);
            """;
        missingOwnerCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        missingOwnerCommand.Parameters.AddWithValue("@biotaId", missingOwnerBiotaId);
        missingOwnerCommand.Parameters.AddWithValue("@shardId", BoundShardId);
        missingOwnerCommand.Parameters.AddWithValue("@ledgerCorrelationId", Guid.NewGuid().ToString());

        var missingOwnerException = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => missingOwnerCommand.ExecuteNonQueryAsync());
        StringAssert.Contains(missingOwnerException.Message, "OwnerId");
    }

    [TestMethod]
    public async Task CloudCustodyBoundary_Deposit_Succeeds_WhenBiotaHasNoWorldPossession()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var ownerId = Guid.NewGuid();
        var ledgerCorrelationId = Guid.NewGuid();

        var record = await boundary.DepositAsync(biotaId, BoundShardId, ownerId, ledgerCorrelationId);

        Assert.AreEqual(biotaId, record.BiotaId);
        Assert.AreEqual(ownerId, record.OwnerId);

        await using var verifyContext = new CloudDbContext(options);
        var persisted = await verifyContext.CloudCustodyRecords.SingleAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(ownerId, persisted.OwnerId);
    }

    [TestMethod]
    public async Task ConcurrentTransactions_WorldPossessionThenCloudCustody_SecondTransactionBlocksThenIsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var worldConnection = new MySqlConnection(_fixture.AceShardConnectionString);
        await worldConnection.OpenAsync();
        await using var worldTransaction = await worldConnection.BeginTransactionAsync();

        // Transaction A: grant world possession, left uncommitted (racing the boundary).
        await GrantContainerAsync(worldConnection, biotaId, containerId: NextBiotaId(), worldTransaction);

        await using var cloudConnection = new MySqlConnection(_fixture.CloudConnectionString);
        await cloudConnection.OpenAsync();

        // Transaction B: attempt a conflicting Cloud custody deposit for the same biota, concurrently.
        var depositTask = InsertCustodyRecordAsync(cloudConnection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid());

        // A non-locking read here would let B race past A's uncommitted row; a deterministically
        // locked check must block B until A resolves.
        var completedFirst = await Task.WhenAny(depositTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreNotSame(
            depositTask,
            completedFirst,
            "Cloud custody deposit must block while a conflicting world-possession transaction is uncommitted, not race past a stale snapshot.");

        await worldTransaction.CommitAsync();

        // Once unblocked, B observes the now-committed world possession and is rejected.
        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(() => depositTask);
        StringAssert.Contains(exception.Message, "world possession");

        Assert.IsTrue(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));
    }

    [TestMethod]
    public async Task ConcurrentTransactions_CloudCustodyThenWorldPossession_SecondTransactionBlocksThenIsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var cloudConnection = new MySqlConnection(_fixture.CloudConnectionString);
        await cloudConnection.OpenAsync();
        await using var cloudTransaction = await cloudConnection.BeginTransactionAsync();

        // Transaction A: deposit into Cloud custody, left uncommitted (racing the boundary).
        await InsertCustodyRecordAsync(cloudConnection, Guid.NewGuid(), biotaId, BoundShardId, Guid.NewGuid(), cloudTransaction);

        // Transaction B: attempt to grant conflicting world possession for the same biota, concurrently.
        var grantTask = AceShardTestData.GrantContainerAsync(_fixture.AceShardConnectionString, biotaId, containerId: NextBiotaId());

        // A non-locking read here would let B race past A's uncommitted row; a deterministically
        // locked check must block B until A resolves.
        var completedFirst = await Task.WhenAny(grantTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.AreNotSame(
            grantTask,
            completedFirst,
            "Granting Container world possession must block while a conflicting Cloud custody transaction is uncommitted, not race past a stale snapshot.");

        await cloudTransaction.CommitAsync();

        // Once unblocked, B observes the now-committed Cloud custody record and is rejected.
        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(() => grantTask);
        StringAssert.Contains(exception.Message, "Cloud custody");

        Assert.IsFalse(await AceShardTestData.HasContainerAsync(_fixture.AceShardConnectionString, biotaId));
    }

    [TestMethod]
    public async Task CloudCustodyBoundary_Deposit_RefusesWithTypedException_WhenBiotaHasWorldPossession()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);
        await AceShardTestData.GrantWielderAsync(_fixture.AceShardConnectionString, biotaId, wielderId: NextBiotaId());

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        await Assert.ThrowsExactlyAsync<CloudCustodyConflictException>(
            () => boundary.DepositAsync(biotaId, BoundShardId, Guid.NewGuid(), Guid.NewGuid()));

        await using var verifyContext = new CloudDbContext(options);
        var count = await verifyContext.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId);
        Assert.AreEqual(0, count);
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);

    private static async Task GrantContainerAsync(
        MySqlConnection connection,
        uint biotaId,
        uint containerId,
        MySqlTransaction? transaction)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO biota_properties_i_i_d (object_Id, type, value)
            VALUES (@objectId, 2, @containerId);
            """;
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@containerId", containerId);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertCustodyRecordAsync(
        MySqlConnection connection,
        Guid id,
        uint biotaId,
        string shardId,
        Guid ownerId,
        MySqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO CloudCustodyRecord (Id, BiotaId, ShardId, OwnerId, LedgerCorrelationId, Version)
            VALUES (@id, @biotaId, @shardId, @ownerId, @ledgerCorrelationId, 1);
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
        command.Parameters.AddWithValue("@biotaId", biotaId);
        command.Parameters.AddWithValue("@shardId", shardId);
        command.Parameters.AddWithValue("@ownerId", ownerId.ToString());
        command.Parameters.AddWithValue("@ledgerCorrelationId", Guid.NewGuid().ToString());
        await command.ExecuteNonQueryAsync();
    }
}
