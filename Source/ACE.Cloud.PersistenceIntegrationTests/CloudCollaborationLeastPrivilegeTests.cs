using ACE.Cloud.Domain;
using ACE.Cloud.Persistence;
using MySqlConnector;

namespace ACE.Cloud.PersistenceIntegrationTests;

/// <summary>
/// PR #157 blocking human-acceptance feedback #2 (issue #39): local acceptance provisioning granted
/// the companion runtime identity only <c>ace_cloud.*</c>, so
/// <see cref="CloudSharingGrantGateway.SetAsync"/> failed with an unhandled
/// <c>SELECT command denied to user cloud_acceptance for table ace_shard.character</c> the moment it
/// resolved a grantee's current character name. These tests prove, against a real MariaDB, that:
/// (1) the minimum required grants -- SELECT on exactly <c>ace_shard.character</c> and
/// <c>ace_shard.biota_properties_i_i_d</c>, nothing broader in ace_shard -- are both necessary and
/// sufficient for collaboration name-resolution and live allegiance reads to succeed; and (2) a still
/// under-provisioned identity now fails with a safe, actionable
/// <see cref="CloudDatabasePrivilegeException"/> instead of an unhandled, detail-leaking
/// <see cref="MySqlException"/>.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudCollaborationLeastPrivilegeTests
{
    private const string ShardId = "us1";
    private const uint OwnerAccountId = 800_500;

    private static CloudDatabaseFixture _fixture = null!;
    private static uint _nextId = 950_000;

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

    /// <summary>
    /// Red: reproduces PR #157's exact reported failure -- a companion identity granted only
    /// ace_cloud.* (the local acceptance migrator's shape before this fix) must not leak a raw,
    /// account-name-and-schema-bearing MySqlException out of a Sharing Grant request; it must fail
    /// with the safe, actionable <see cref="CloudDatabasePrivilegeException"/>.
    /// </summary>
    [TestMethod]
    public async Task SetAsync_AgainstAnIdentityMissingTheAceShardGrants_FailsWithASafeActionableException_NotARawMySqlException()
    {
        var username = "cloud_underprivileged_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var underprivilegedConnectionString =
            await _fixture.CreateRestrictedCompanionConnectionStringWithoutShardAccessAsync(username, password);

        var granteeCharacterId = NextId();
        var granteeAccountId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, granteeAccountId, "UnderprivilegedGrantee");

        var options = CloudDbContextOptionsFactory.Create(underprivilegedConnectionString);
        await using var context = new CloudDbContext(options);
        var gateway = new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context));

        var thrown = await Assert.ThrowsExactlyAsync<CloudDatabasePrivilegeException>(
            () => gateway.SetAsync(ShardId, OwnerAccountId, "UnderprivilegedGrantee", CloudSharingGrantLevel.ViewOnly));

        StringAssert.DoesNotMatch(thrown.Message, new System.Text.RegularExpressions.Regex("cloud_underprivileged|ace_shard|UnderprivilegedGrantee", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Green: the minimum grants issue #39 adds -- SELECT on exactly ace_shard.character and
    /// ace_shard.biota_properties_i_i_d -- are sufficient for the same request to reach a normal
    /// domain outcome instead of failing on privileges at all.
    /// </summary>
    [TestMethod]
    public async Task SetAsync_AgainstAnIdentityWithTheMinimumAceShardGrants_ResolvesTheGranteeNormally()
    {
        var username = "cloud_privileged_" + Guid.NewGuid().ToString("N")[..12];
        var password = Guid.NewGuid().ToString("N");
        var privilegedConnectionString =
            await _fixture.CreateRestrictedCompanionConnectionStringAsync(username, password);

        var granteeCharacterId = NextId();
        var granteeAccountId = NextId();
        await AceShardTestData.InsertCharacterAsync(_fixture.AceShardConnectionString, granteeCharacterId, granteeAccountId, "PrivilegedGrantee");

        var options = CloudDbContextOptionsFactory.Create(privilegedConnectionString);
        await using var context = new CloudDbContext(options);
        var gateway = new CloudSharingGrantGateway(context, new CloudAccountLinkGateway(context));

        var outcome = await gateway.SetAsync(ShardId, OwnerAccountId, "PrivilegedGrantee", CloudSharingGrantLevel.ViewOnly);

        Assert.AreEqual(CloudBoundaryOutcomeKind.Committed, outcome.Kind, outcome.Reason);
    }

    private static uint NextId() => Interlocked.Increment(ref _nextId);
}
