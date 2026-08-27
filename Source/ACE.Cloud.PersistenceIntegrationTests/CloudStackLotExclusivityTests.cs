using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green tests for issue #5's conservation invariants (ARCH-010, ARCH-011, INV-001,
/// docs/adr/0002-defer-native-materialization-for-partial-stacks.md): a Cloud Stack Lot's quantity
/// must always be positive, and the sum of every lot backed by one stackable Cloud Custody Record
/// must never exceed that record's TotalQuantity. These tests prove the database itself rejects an
/// invalid state, not just application code, mirroring
/// <see cref="CloudCustodyRecordExclusivityTests"/>'s style for issue #2.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudStackLotExclusivityTests
{
    private const string BoundShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 700_000;

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
        await CloudBoundaryTestFixtureData.ResetAsync(_fixture.CloudConnectionString, BoundShardId);
    }

    [TestMethod]
    public async Task NonPositiveLotQuantity_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyRecordId = await InsertStackCustodyRecordAsync(biotaId, totalQuantity: 10);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertLotAsync(connection, custodyRecordId, BoundShardId, Guid.NewGuid(), quantity: 0));

        StringAssert.Contains(exception.Message, "Quantity");
    }

    [TestMethod]
    public async Task NegativeLotQuantity_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyRecordId = await InsertStackCustodyRecordAsync(biotaId, totalQuantity: 10);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertLotAsync(connection, custodyRecordId, BoundShardId, Guid.NewGuid(), quantity: -5));

        StringAssert.Contains(exception.Message, "Quantity");
    }

    [TestMethod]
    public async Task LotQuantitiesExceedingBackingStackTotal_AreRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyRecordId = await InsertStackCustodyRecordAsync(biotaId, totalQuantity: 10);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await InsertLotAsync(connection, custodyRecordId, BoundShardId, Guid.NewGuid(), quantity: 7);

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertLotAsync(connection, custodyRecordId, BoundShardId, Guid.NewGuid(), quantity: 4));

        StringAssert.Contains(exception.Message, "exceed");
    }

    [TestMethod]
    public async Task IncreasingALotBeyondTheRemainingUnallocatedTotal_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyRecordId = await InsertStackCustodyRecordAsync(biotaId, totalQuantity: 10);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var lotId = Guid.NewGuid();
        await InsertLotAsync(connection, custodyRecordId, BoundShardId, Guid.NewGuid(), quantity: 6, lotId);
        await InsertLotAsync(connection, custodyRecordId, BoundShardId, Guid.NewGuid(), quantity: 4);

        // The stack is already fully allocated (6 + 4 = 10); growing either lot must be rejected.
        await using var update = connection.CreateCommand();
        update.CommandText = "UPDATE CloudStackLot SET Quantity = 7, Version = 2 WHERE Id = @id;";
        update.Parameters.AddWithValue("@id", lotId.ToString());

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(() => update.ExecuteNonQueryAsync());
        StringAssert.Contains(exception.Message, "exceed");
    }

    [TestMethod]
    public async Task LotReferencingNonStackCustodyRecord_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        Guid nonStackCustodyRecordId;
        await using (var context = new CloudDbContext(options))
        {
            var record = new CloudCustodyRecord(biotaId, BoundShardId, Guid.NewGuid(), Guid.NewGuid());
            context.CloudCustodyRecords.Add(record);
            await context.SaveChangesAsync();
            nonStackCustodyRecordId = record.Id;
        }

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(
            () => InsertLotAsync(connection, nonStackCustodyRecordId, BoundShardId, Guid.NewGuid(), quantity: 1));

        StringAssert.Contains(exception.Message, "not a stack");
    }

    [TestMethod]
    public async Task StackCustodyRecord_RequiresNullOwnerId_AndPositiveTotalQuantity()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        // Both OwnerId and TotalQuantity set is rejected: a record is exclusively non-stack (one
        // owner) or a stack (quantity lots), never both (CONTEXT.md's Cloud Custody Record entry).
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CloudCustodyRecord (Id, BiotaId, ShardId, OwnerId, TotalQuantity, LedgerCorrelationId, Version)
            VALUES (@id, @biotaId, @shardId, @ownerId, @totalQuantity, @ledgerCorrelationId, 1);
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@biotaId", biotaId);
        command.Parameters.AddWithValue("@shardId", BoundShardId);
        command.Parameters.AddWithValue("@ownerId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@totalQuantity", 5);
        command.Parameters.AddWithValue("@ledgerCorrelationId", Guid.NewGuid().ToString());

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(() => command.ExecuteNonQueryAsync());
        StringAssert.Contains(exception.Message, "CK_CloudCustodyRecord_OwnerXorStack");
    }

    [TestMethod]
    public async Task StackCustodyRecord_WithNeitherOwnerNorQuantity_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CloudCustodyRecord (Id, BiotaId, ShardId, OwnerId, TotalQuantity, LedgerCorrelationId, Version)
            VALUES (@id, @biotaId, @shardId, NULL, NULL, @ledgerCorrelationId, 1);
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@biotaId", biotaId);
        command.Parameters.AddWithValue("@shardId", BoundShardId);
        command.Parameters.AddWithValue("@ledgerCorrelationId", Guid.NewGuid().ToString());

        var exception = await Assert.ThrowsExactlyAsync<MySqlException>(() => command.ExecuteNonQueryAsync());
        StringAssert.Contains(exception.Message, "CK_CloudCustodyRecord_OwnerXorStack");
    }

    [TestMethod]
    public async Task DeletingACustodyRecordWithSurvivingLots_IsRejected()
    {
        var biotaId = NextBiotaId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaId);

        var custodyRecordId = await InsertStackCustodyRecordAsync(biotaId, totalQuantity: 10);

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();
        await InsertLotAsync(connection, custodyRecordId, BoundShardId, Guid.NewGuid(), quantity: 10);

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM CloudCustodyRecord WHERE Id = @id;";
        delete.Parameters.AddWithValue("@id", custodyRecordId.ToString());

        await Assert.ThrowsExactlyAsync<MySqlException>(() => delete.ExecuteNonQueryAsync());
    }

    private static async Task<Guid> InsertStackCustodyRecordAsync(uint biotaId, int totalQuantity)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var record = CloudCustodyRecord.CreateStack(biotaId, BoundShardId, totalQuantity, Guid.NewGuid());
        context.CloudCustodyRecords.Add(record);
        await context.SaveChangesAsync();
        return record.Id;
    }

    private static async Task InsertLotAsync(
        MySqlConnection connection, Guid custodyRecordId, string shardId, Guid ownerId, int quantity, Guid? lotId = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CloudStackLot (Id, CustodyRecordId, ShardId, OwnerId, Quantity, Version)
            VALUES (@id, @custodyRecordId, @shardId, @ownerId, @quantity, 1);
            """;
        command.Parameters.AddWithValue("@id", (lotId ?? Guid.NewGuid()).ToString());
        command.Parameters.AddWithValue("@custodyRecordId", custodyRecordId.ToString());
        command.Parameters.AddWithValue("@shardId", shardId);
        command.Parameters.AddWithValue("@ownerId", ownerId.ToString());
        command.Parameters.AddWithValue("@quantity", quantity);
        await command.ExecuteNonQueryAsync();
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);
}
