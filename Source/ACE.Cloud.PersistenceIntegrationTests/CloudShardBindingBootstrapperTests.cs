using ACE.Cloud.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// Red -> Green for issue #34's blocking defect #2: ACE.Cloud.LocalAcceptanceMigrator ran schema
/// migrations but never seeded the mandatory singleton CloudShardBinding row, leaving every companion
/// startup check permanently reporting "Operator Bootstrap has not completed" even right after a
/// fresh migrate. Proves first run, an idempotent repeat run, and a refused mismatch -- against a
/// real disposable MariaDB instance, not a mock.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudShardBindingBootstrapperTests
{
    private const string ShardId = "acceptance-shard";
    private const string SchemaVersion = "0.1.0";
    private const string AceExtensionVersion = "0.1.0";
    private const string ContractProtocolVersion = "0.1.0";

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

    [TestInitialize]
    public async Task TestInitialize()
    {
        await using var connection = new MySqlConnection(_fixture.CloudConnectionString);
        await connection.OpenAsync();

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM CloudShardBinding;";
        await delete.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task BootstrapAsync_FirstRun_CreatesTheSingletonRow()
    {
        var result = await CloudShardBindingBootstrapper.BootstrapAsync(
            _fixture.CloudConnectionString, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion);

        Assert.IsTrue(result.WasCreated);

        var binding = await ReadBindingAsync();
        Assert.IsNotNull(binding);
        Assert.AreEqual(ShardId, binding!.ShardId);
        Assert.AreEqual(SchemaVersion, binding.SchemaVersion);
        Assert.AreEqual(AceExtensionVersion, binding.AceExtensionVersion);
        Assert.AreEqual(ContractProtocolVersion, binding.ContractProtocolVersion);
    }

    [TestMethod]
    public async Task BootstrapAsync_RepeatRunWithIdenticalValues_IsAnIdempotentNoOp()
    {
        var first = await CloudShardBindingBootstrapper.BootstrapAsync(
            _fixture.CloudConnectionString, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion);
        Assert.IsTrue(first.WasCreated);

        var second = await CloudShardBindingBootstrapper.BootstrapAsync(
            _fixture.CloudConnectionString, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion);
        Assert.IsFalse(second.WasCreated, "A repeat run with identical values must be a no-op, not a second insert.");

        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        Assert.AreEqual(1, await context.CloudShardBindings.CountAsync());
    }

    [TestMethod]
    public async Task BootstrapAsync_MismatchedShardId_ThrowsAndNeverOverwritesTheExistingRow()
    {
        await CloudShardBindingBootstrapper.BootstrapAsync(
            _fixture.CloudConnectionString, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion);

        await Assert.ThrowsExceptionAsync<CloudShardBindingMismatchException>(() =>
            CloudShardBindingBootstrapper.BootstrapAsync(
                _fixture.CloudConnectionString, "a-different-shard", SchemaVersion, AceExtensionVersion, ContractProtocolVersion));

        var binding = await ReadBindingAsync();
        Assert.IsNotNull(binding);
        Assert.AreEqual(ShardId, binding!.ShardId, "A mismatch must never overwrite the existing shard binding.");
    }

    [TestMethod]
    public async Task BootstrapAsync_MismatchedVersions_ThrowsAndNeverOverwritesTheExistingRow()
    {
        await CloudShardBindingBootstrapper.BootstrapAsync(
            _fixture.CloudConnectionString, ShardId, SchemaVersion, AceExtensionVersion, ContractProtocolVersion);

        await Assert.ThrowsExceptionAsync<CloudShardBindingMismatchException>(() =>
            CloudShardBindingBootstrapper.BootstrapAsync(
                _fixture.CloudConnectionString, ShardId, SchemaVersion, "9.9.9-does-not-match", ContractProtocolVersion));

        var binding = await ReadBindingAsync();
        Assert.IsNotNull(binding);
        Assert.AreEqual(AceExtensionVersion, binding!.AceExtensionVersion, "A version mismatch must never overwrite the existing shard binding.");
    }

    private static async Task<CloudShardBinding?> ReadBindingAsync()
    {
        var options = CloudDbContextOptionsFactory.Create(_fixture.CloudConnectionString);
        await using var context = new CloudDbContext(options);
        return await context.CloudShardBindings.AsNoTracking().SingleOrDefaultAsync();
    }
}
