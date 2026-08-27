using ACE.Cloud.Persistence;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green test for issue #4's Red section: "Test... deadlock retry." Forces a genuine
/// MariaDB deadlock (error 1213) between two concurrent transactions locking the same two rows in
/// opposite order, and proves <see cref="CloudBoundaryRetry"/> retries the loser until both sides
/// succeed rather than surfacing the raw provider exception.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudBoundaryRetryTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 600_000;

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
        await CloudBoundaryTestFixtureData.ResetAsync(_fixture.CloudConnectionString, ShardId);
    }

    [TestMethod]
    public async Task ExecuteAsync_RetriesAGenuineDeadlock_UntilBothConcurrentTransactionsSucceed()
    {
        var recordIdA = Guid.NewGuid();
        var recordIdB = Guid.NewGuid();

        var biotaIdA = NextId();
        var biotaIdB = NextId();
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaIdA);
        await AceShardTestData.InsertBiotaAsync(_fixture.AceShardConnectionString, biotaIdB);

        await InsertCustodyRecordAsync(recordIdA, biotaIdA);
        await InsertCustodyRecordAsync(recordIdB, biotaIdB);

        // Task 1 locks A then B; Task 2 locks B then A. Both delay between their two locks so the
        // opposite-order acquisition overlaps and MariaDB's deadlock detector kills one side.
        var task1 = CloudBoundaryRetry.ExecuteAsync(() => LockBothRowsInOrderAsync(recordIdA, recordIdB));
        var task2 = CloudBoundaryRetry.ExecuteAsync(() => LockBothRowsInOrderAsync(recordIdB, recordIdA));

        var results = await Task.WhenAll(task1, task2);

        Assert.IsTrue(results.All(r => r.Kind == CloudBoundaryOutcomeKind.Committed), "Both sides of a genuine deadlock must eventually commit once the retry wrapper retries the loser.");
    }

    private async Task<CloudBoundaryOutcome<bool>> LockBothRowsInOrderAsync(Guid firstId, Guid secondId)
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await LockRowAsync(connection, transaction, firstId);
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        await LockRowAsync(connection, transaction, secondId);

        await transaction.CommitAsync();
        return CloudBoundaryOutcome<bool>.Committed(true);
    }

    private static async Task LockRowAsync(MySqlConnection connection, MySqlTransaction transaction, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM CloudCustodyRecord WHERE Id = @id FOR UPDATE;";
        command.Parameters.AddWithValue("@id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
    }

    private async Task InsertCustodyRecordAsync(Guid id, uint biotaId)
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CloudCustodyRecord (Id, BiotaId, ShardId, OwnerId, LedgerCorrelationId, Version)
            VALUES (@id, @biotaId, @shardId, @ownerId, @ledgerCorrelationId, 1);
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
        command.Parameters.AddWithValue("@biotaId", biotaId);
        command.Parameters.AddWithValue("@shardId", ShardId);
        command.Parameters.AddWithValue("@ownerId", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@ledgerCorrelationId", Guid.NewGuid().ToString());
        await command.ExecuteNonQueryAsync();
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
