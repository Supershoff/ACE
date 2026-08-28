using ACE.Cloud.Persistence;
using ACE.Cloud.PersistenceIntegrationTests;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.AceCustodyIntegrationTests;

/// <summary>
/// Red -&gt; Green tests for issue #13's ACE-side deposit sequence: ACE's Cloud Custodian handler must
/// clear a candidate item's world possession (Container) in ace_shard, durably and synchronously,
/// before calling <see cref="CloudCustodyBoundary.DepositAsync"/>/<c>DepositStackAsync</c>
/// (<c>Player_CloudCustodian.cs</c>'s doc comment; <see cref="CloudCustodyBoundary"/>'s own doc
/// comment: "ACE's Cloud Custodian path is responsible for that earlier step"). These tests prove
/// the exact two-step sequence that handler performs, at the same ACE.Database/Cloud persistence
/// seam <see cref="CloudCustodyAceSeamsTests"/> already exercises, without needing a live
/// ACE.Server WorldObject/Player (ARCH-002, ARCH-005, DEP-002).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodianDepositAceSeamsTests
{
    private const string ShardId = "us1";
    private const short ContainerPropertyType = 2; // PropertyInstanceId.Container

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextBiotaId = 900_000;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();

        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using var insertBinding = connection.CreateCommand();
        insertBinding.CommandText = """
            INSERT INTO CloudShardBinding (Id, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion)
            VALUES (1, @shardId, '0.1.0', '0.1.0', '0.1.0');
            """;
        insertBinding.Parameters.AddWithValue("@shardId", ShardId);
        await insertBinding.ExecuteNonQueryAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _fixture.DisposeAsync();
    }

    private static uint NextBiotaId() => Interlocked.Increment(ref _nextBiotaId);

    private static async Task InsertBiotaAsync(uint biotaId, uint containerId)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO biota (id, weenie_Class_Id, weenie_Type, populated_Collection_Flags)
            VALUES (@id, 1, 1, 0);
            """;
        command.Parameters.AddWithValue("@id", biotaId);
        await command.ExecuteNonQueryAsync();

        await using var grantContainer = connection.CreateCommand();
        grantContainer.CommandText = """
            INSERT INTO biota_properties_i_i_d (object_Id, type, value)
            VALUES (@objectId, @type, @containerId);
            """;
        grantContainer.Parameters.AddWithValue("@objectId", biotaId);
        grantContainer.Parameters.AddWithValue("@type", ContainerPropertyType);
        grantContainer.Parameters.AddWithValue("@containerId", containerId);
        await grantContainer.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Mirrors what <c>Player_CloudCustodian.SynchronouslyPersist</c> achieves in production by
    /// removing the item from the player's in-memory inventory (which clears its Container property)
    /// and then durably saving the resulting biota before the boundary call: the persisted Container
    /// row is gone.
    /// </summary>
    private static async Task ClearContainerAsync(uint biotaId)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM biota_properties_i_i_d WHERE object_Id = @objectId AND type = @type;";
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@type", ContainerPropertyType);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> HasContainerAsync(uint biotaId)
    {
        await using var connection = new MySqlConnection(_fixture.AceShardConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM biota_properties_i_i_d WHERE object_Id = @objectId AND type = @type;";
        command.Parameters.AddWithValue("@objectId", biotaId);
        command.Parameters.AddWithValue("@type", ContainerPropertyType);

        var count = (long)(await command.ExecuteScalarAsync())!;
        return count > 0;
    }

    private static async Task<int> CountCustodyRecordsAsync(uint biotaId)
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        return await context.CloudCustodyRecords.CountAsync(r => r.BiotaId == biotaId);
    }

    [TestMethod]
    public async Task Deposit_AfterClearingContainer_CreatesCustodyRecord_WithNoWorldPossessionLeft()
    {
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId, containerId: 12345);
        await ClearContainerAsync(biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.IsFalse(await HasContainerAsync(biotaId), "A deposited item must have no world possession left (ARCH-005).");
        Assert.AreEqual(1, await CountCustodyRecordsAsync(biotaId));
    }

    [TestMethod]
    public async Task Deposit_WhileContainerIsStillPresent_IsRejected_AndCreatesNoCustodyRecord()
    {
        // Simulates a handler bug where the synchronous ace_shard save was skipped: the boundary's
        // own precondition must still refuse the deposit rather than create ambiguous custody
        // (simultaneous world possession and Cloud custody, AGENTS.md's "Custody authority and
        // conservation" rule).
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId, containerId: 12345);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Conflict, outcome.Kind);
        Assert.IsTrue(await HasContainerAsync(biotaId), "The item must remain exactly as it was: still world-possessed.");
        Assert.AreEqual(0, await CountCustodyRecordsAsync(biotaId));
    }

    [TestMethod]
    public async Task Deposit_RepeatedWithTheSameIdempotencyKey_ReplaysTheOriginalRecord_NeverDuplicates()
    {
        // DEP-002/ARCH-006: a retried deposit attempt for the same biota (for example a resent game
        // action after a network hiccup) must produce exactly one authoritative custody record.
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId, containerId: 12345);
        await ClearContainerAsync(biotaId);

        var idempotencyKey = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);

        CloudBoundaryOutcome<CloudCustodyRecord> first;
        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            first = await boundary.DepositAsync(biotaId, ShardId, ownerId, idempotencyKey);
        }

        CloudBoundaryOutcome<CloudCustodyRecord> second;
        await using (var context = new CloudDbContext(options))
        {
            var boundary = new CloudCustodyBoundary(context);
            second = await boundary.DepositAsync(biotaId, ShardId, ownerId, idempotencyKey);
        }

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, first.Kind, first.Reason);
        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, second.Kind, second.Reason);
        Assert.AreEqual(first.Value!.Id, second.Value!.Id);
        Assert.AreEqual(1, await CountCustodyRecordsAsync(biotaId));
    }

    [TestMethod]
    public async Task DepositStack_AfterClearingContainer_CreatesAStackCustodyRecordAndLot_WithNoWorldPossessionLeft()
    {
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId, containerId: 12345);
        await ClearContainerAsync(biotaId);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), quantity: 15, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.IsFalse(await HasContainerAsync(biotaId));
        Assert.AreEqual(15, outcome.Value!.Lot.Quantity);
        Assert.AreEqual(1, await CountCustodyRecordsAsync(biotaId));
    }
}
