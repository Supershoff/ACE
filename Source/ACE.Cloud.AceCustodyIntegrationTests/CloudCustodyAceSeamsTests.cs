using System.Threading;
using ACE.Cloud.Persistence;
using ACE.Cloud.PersistenceIntegrationTests;
using ACE.Common;
using ACE.Database;
using ACE.Database.Models.Shard;
using ACE.Entity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.AceCustodyIntegrationTests;

/// <summary>
/// Red -&gt; Green tests for issue #3: teaching ACE's own persistence, integrity, cleanup, and
/// GUID-manager code paths (ARCH-002, ARCH-005, ARCH-010, ARCH-011) that a Cloud Custody Record is
/// a first-class off-world possession state, not an orphaned biota.
///
/// Unlike <c>ACE.Cloud.PersistenceIntegrationTests</c> (which deliberately avoids depending on
/// ACE.Database to keep the Cloud persistence library decoupled from live ACE world objects), this
/// project exists specifically to drive the REAL production ACE.Database code
/// (<see cref="ShardDatabaseOfflineTools"/>, <see cref="ShardDatabase"/>) against a Cloud-custodied
/// fixture, because that is exactly the seam this issue is about. It reuses
/// <see cref="CloudDatabaseFixture"/> rather than duplicating its Testcontainers/schema bootstrap.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCustodyAceSeamsTests
{
    private const string ShardId = "us1";

    private static CloudDatabaseFixture _fixture = null!;

    // Real dynamic-range GUIDs (ACE.Entity.ObjectGuid.DynamicMin/DynamicMax), matching what
    // GuidManager's DynamicGuidAllocator actually scans, so the GUID-reservation tests below are
    // proving the real invariant rather than an arbitrary test ID.
    private static uint _nextBiotaId = ObjectGuid.DynamicMin + 500_000;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext context)
    {
        _fixture = await CloudDatabaseFixture.StartAsync();

        // ShardDatabaseOfflineTools/ShardDatabase/GuidManager all resolve their connection through
        // ConfigManager.Config.MySql.Shard (there is no context-parameter path for the individual
        // PurgeBiota calls inside a Parallel.ForEach, since a DbContext is not thread-safe). Pointing
        // this at the fixture's disposable ace_shard database lets every test below call the exact
        // same static entry points ACE.Server.Program and GuidManager call in production.
        var shardConnection = new MySqlConnectionStringBuilder(_fixture.AceShardConnectionString);

        var configuration = new MasterConfiguration();
        configuration.MySql.Shard.Host = shardConnection.Server;
        configuration.MySql.Shard.Port = shardConnection.Port;
        configuration.MySql.Shard.Database = shardConnection.Database;
        configuration.MySql.Shard.Username = shardConnection.UserID;
        configuration.MySql.Shard.Password = shardConnection.Password;

        ConfigManager.Initialize(configuration);

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

    private static async Task InsertBiotaAsync(uint biotaId)
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
    }

    private static async Task DepositAsync(uint biotaId)
    {
        var cloudOptions = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(cloudOptions);
        var boundary = new CloudCustodyBoundary(context);
        await boundary.DepositAsync(biotaId, ShardId, Guid.NewGuid(), Guid.NewGuid());
    }

    private static async Task<HashSet<uint>> GetCustodiedBiotaIdsAsync()
    {
        var cloudOptions = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(cloudOptions);
        return (await context.CloudCustodyRecords.Select(r => r.BiotaId).ToListAsync()).ToHashSet();
    }

    private static async Task<bool> BiotaExistsAsync(uint biotaId)
    {
        using var context = new ShardDbContext();
        return await context.Biota.AsNoTracking().AnyAsync(b => b.Id == biotaId);
    }

    [TestMethod]
    public async Task PurgeOrphanedBiotas_ExcludesCloudCustodiedBiota_WhenGivenTheCustodySet()
    {
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId);
        await DepositAsync(biotaId);

        var custodiedBiotaIds = await GetCustodiedBiotaIdsAsync();

        using var context = new ShardDbContext();
        ShardDatabaseOfflineTools.PurgeOrphanedBiotasInParallel(context, out _, custodiedBiotaIds);

        // Assert on this specific biota's survival rather than the aggregate purged count: the
        // fixture's ace_shard database is shared across every test in this class, so the count also
        // reflects whatever other orphans this or earlier tests happened to leave behind.
        Assert.IsTrue(await BiotaExistsAsync(biotaId), "A Cloud-custodied biota must survive orphan cleanup (ARCH-005).");
    }

    [TestMethod]
    public async Task PurgeOrphanedBiotas_StillPurgesAnOrdinaryOrphan_NotUnderCloudCustody()
    {
        // Regression guard: excluding custodied biotas must not stop real orphan cleanup from
        // working, matching the pre-existing Allegiance exclusion this mirrors.
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId);

        using var context = new ShardDbContext();
        ShardDatabaseOfflineTools.PurgeOrphanedBiotasInParallel(context, out _, cloudCustodiedBiotaIds: null);

        Assert.IsFalse(await BiotaExistsAsync(biotaId));
    }

    [TestMethod]
    public async Task PurgeOrphanedBiotas_AtServerStartup_PreservesCloudCustodyRecord_EvenWithoutAnExclusionList()
    {
        // Exercises the exact zero-argument entry point ACE.Server.Program calls at every boot
        // (Offline.PurgeOrphanedBiotas), simulating a caller that has no idea Cloud Mule exists.
        // The ace_shard database trigger from the Cloud schema migration is the last line of
        // defense here (AGENTS.md: "database constraints plus deterministic locked validation").
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId);
        await DepositAsync(biotaId);

        ShardDatabaseOfflineTools.PurgeOrphanedBiotasInParallel(out _);

        Assert.IsTrue(await BiotaExistsAsync(biotaId));
    }

    [TestMethod]
    public async Task PurgeBiota_ReturnsFalse_AndDoesNotDeleteTheBiota_WhenItIsCloudCustodied()
    {
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId);
        await DepositAsync(biotaId);

        var purged = ShardDatabaseOfflineTools.PurgeBiota(biotaId, "test");

        Assert.IsFalse(purged, "PurgeBiota must not silently report success when the database rejects the delete.");
        Assert.IsTrue(await BiotaExistsAsync(biotaId));
    }

    [TestMethod]
    public async Task PurgeBiota_StillPurgesAnOrdinaryBiota_NotUnderCloudCustody()
    {
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId);

        var purged = ShardDatabaseOfflineTools.PurgeBiota(biotaId, "test");

        Assert.IsTrue(purged);
        Assert.IsFalse(await BiotaExistsAsync(biotaId));
    }

    [TestMethod]
    public async Task CloudCustodiedBiota_GuidRemainsReserved_AcrossASimulatedRestart()
    {
        // GuidManager's allocators (Source/ACE.Server/Managers/GuidManager.cs) determine both the
        // in-use ceiling and free sequence gaps purely from ace_shard.biota row presence
        // (ShardDatabase.GetMaxGuidFoundInRange / GetSequenceGaps), with no Container/Wielder/
        // Location predicate at all. As long as the biota row survives, its GUID can never be a
        // gap. This proves that survival holds across a full simulated restart: cleanup runs (as
        // Program.cs does at every boot), then a brand-new ShardDatabase instance (simulating
        // GuidManager.Initialize() re-scanning from a cold start after the process restarted)
        // still reports the GUID as the reserved max in its range, never free.
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId);
        await DepositAsync(biotaId);

        var custodiedBiotaIds = await GetCustodiedBiotaIdsAsync();

        // "Restart": the real startup cleanup pass runs against a brand-new context/connection.
        using (var context = new ShardDbContext())
        {
            ShardDatabaseOfflineTools.PurgeOrphanedBiotasInParallel(context, out _, custodiedBiotaIds);
        }

        // "Cold start" GUID scan: the exact production method GuidManager's allocators call,
        // scoped to exactly this GUID (not a wider neighboring range) so the assertion is immune
        // to other biotas this shared fixture's other test methods may have inserted nearby.
        var maxGuidInRange = new ShardDatabase().GetMaxGuidFoundInRange(biotaId, biotaId);

        Assert.AreEqual(biotaId, maxGuidInRange, "A Cloud-custodied biota's GUID must still be found (never a free gap) after a restart.");
    }

    [TestMethod]
    public async Task Deposit_NeverAllocatesOrConsumesANativeGuid_SoAProjectedStackLotCannotEither()
    {
        // ARCH-010/ARCH-011: a projected Cloud Stack Lot must not consume a native GUID until ACE
        // materializes it. CloudStackLot itself does not exist yet (a later issue adds it, per
        // docs/adr/0002-defer-native-materialization-for-partial-stacks.md); what can be proven
        // now is the invariant its future implementation must preserve: the only Cloud
        // custody-creation path that exists today, CloudCustodyBoundary.DepositAsync, reuses the
        // existing native biota's GUID and never allocates/inserts a new ace_shard.biota row.
        var biotaId = NextBiotaId();
        await InsertBiotaAsync(biotaId);

        var biotaCountBefore = new ShardDatabase().GetBiotaCount();

        await DepositAsync(biotaId);

        var biotaCountAfter = new ShardDatabase().GetBiotaCount();

        Assert.AreEqual(biotaCountBefore, biotaCountAfter, "Cloud custody must never allocate a new native biota/GUID.");
    }
}
