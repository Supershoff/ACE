using ACE.Cloud.Persistence;
using ACE.Cloud.PersistenceIntegrationTests;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.AceCustodyIntegrationTests;

/// <summary>
/// Red -&gt; Green tests for issue #13's ACE-side deposit sequence: <see cref="CloudCustodyBoundary.DepositAsync"/>
/// and <c>DepositStackAsync</c> atomically remove a candidate item's remaining world possession
/// (Container) in ace_shard as part of the same MariaDB transaction that creates its Cloud Custody
/// Record, so ACE's Cloud Custodian handler (<c>Player_CloudCustodian.cs</c>) never needs -- and must
/// not perform -- a separate, already-committed removal beforehand (AC Cloud Mule review of issue
/// #13, finding 1: that earlier two-step design left a window where the removal could commit without
/// the Cloud custody record, permanently orphaning the biota). These tests prove the boundary's
/// atomic contract at the same ACE.Database/Cloud persistence seam <see cref="CloudCustodyAceSeamsTests"/>
/// already exercises, without needing a live ACE.Server WorldObject/Player (ARCH-002, ARCH-005, DEP-002).
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
    /// Seeds a biota whose Container row is already gone, so a deposit call exercises the
    /// idempotent/no-op branch of the boundary's world-possession removal (it deletes zero rows
    /// instead of one) rather than the branch that actually clears a live Container.
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
    public async Task Deposit_WhileContainerIsStillPresent_AtomicallyRemovesPossessionAndCreatesCustodyRecord()
    {
        // ACE's Cloud Custodian handler no longer clears the item's Container itself before calling
        // the boundary (that separate, already-committed removal was the P0 orphan-window defect:
        // a crash or a rejected Cloud commit between that removal and the custody-record creation
        // left the biota with neither world possession nor Cloud custody). The boundary must instead
        // remove the still-present Container atomically with creating the Cloud Custody Record.
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId, containerId: 12345);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.IsFalse(await HasContainerAsync(biotaId), "A committed deposit must atomically remove the biota's remaining world possession.");
        Assert.AreEqual(1, await CountCustodyRecordsAsync(biotaId));
    }

    [TestMethod]
    public async Task DepositStack_WhileContainerIsStillPresent_AtomicallyRemovesPossessionAndCreatesCustodyRecord()
    {
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId, containerId: 12345);

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        var boundary = new CloudCustodyBoundary(context);

        var outcome = await boundary.DepositStackAsync(biotaId, ShardId, Guid.NewGuid(), quantity: 15, Guid.NewGuid());

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
        Assert.IsFalse(await HasContainerAsync(biotaId), "A committed stack deposit must atomically remove the biota's remaining world possession.");
        Assert.AreEqual(15, outcome.Value!.Lot.Quantity);
        Assert.AreEqual(1, await CountCustodyRecordsAsync(biotaId));
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
