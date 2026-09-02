using ACE.Common;
using ACE.Server.Managers;

namespace ACE.Cloud.ServerSeamsTests;

/// <summary>
/// AC Cloud Mule issue #17, VAULT-005: <see cref="CloudIdentityEventManager.CheckMonarchDeletion"/>
/// must never silently allow a monarch deletion it cannot actually verify is safe -- a non-monarch
/// is always allowed without ever touching AC Cloud Mule, but once AC Cloud Mule is enabled and the
/// character is a monarch, an unreachable Cloud database or missing configuration must fail safe by
/// blocking rather than guessing "allowed" (the unrecoverable outcome this guard exists to prevent).
/// Mutates the process-global <see cref="ConfigManager.Config"/>, so this class does not parallelize
/// with itself.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CloudIdentityEventManagerTests
{
    [TestMethod]
    public void CheckMonarchDeletion_WhenCloudMuleDisabled_AlwaysAllows()
    {
        ConfigManager.Initialize(new MasterConfiguration());

        var decision = CloudIdentityEventManager.CheckMonarchDeletion(characterId: 0x80000001, isMonarch: true);

        Assert.IsTrue(decision.IsAllowed);
    }

    [TestMethod]
    public void CheckMonarchDeletion_ForANonMonarch_AlwaysAllows_EvenWhenCloudMuleIsEnabled()
    {
        var configuration = new MasterConfiguration();
        configuration.CloudMule.Enabled = true;
        configuration.CloudMule.ShardId = "us1";
        // An unreachable database would otherwise cause this to fail safe by blocking; a non-monarch
        // must never even attempt that check in the first place.
        configuration.MySql.Cloud.Host = "127.0.0.1";
        configuration.MySql.Cloud.Port = 1;
        ConfigManager.Initialize(configuration);

        var decision = CloudIdentityEventManager.CheckMonarchDeletion(characterId: 0x80000001, isMonarch: false);

        Assert.IsTrue(decision.IsAllowed);
    }

    [TestMethod]
    public void CheckMonarchDeletion_WhenCloudMuleEnabledButShardIdIsMissing_FailsSafeByBlocking()
    {
        var configuration = new MasterConfiguration();
        configuration.CloudMule.Enabled = true;
        configuration.CloudMule.ShardId = "";
        ConfigManager.Initialize(configuration);

        var decision = CloudIdentityEventManager.CheckMonarchDeletion(characterId: 0x80000001, isMonarch: true);

        Assert.IsFalse(decision.IsAllowed);
        Assert.IsFalse(string.IsNullOrWhiteSpace(decision.Reason));
    }

    [TestMethod]
    public void CheckMonarchDeletion_WhenCloudMuleEnabledButTheDatabaseIsUnreachable_FailsSafeByBlocking()
    {
        var configuration = new MasterConfiguration();
        configuration.CloudMule.Enabled = true;
        configuration.CloudMule.ShardId = "us1";
        configuration.MySql.Cloud.Host = "127.0.0.1";
        configuration.MySql.Cloud.Port = 1; // nothing listens here
        configuration.MySql.Cloud.Database = "ace_cloud";
        configuration.MySql.Cloud.Username = "root";
        configuration.MySql.Cloud.Password = "root";
        ConfigManager.Initialize(configuration);

        var decision = CloudIdentityEventManager.CheckMonarchDeletion(characterId: 0x80000001, isMonarch: true);

        Assert.IsFalse(decision.IsAllowed, "An unverifiable Allegiance Vault status must never resolve to an allowed deletion (VAULT-005).");
        Assert.IsFalse(string.IsNullOrWhiteSpace(decision.Reason));
    }

    /// <summary>
    /// Issue #39's self-heal fix: <see cref="CloudIdentityEventManager.PublishCharacterLoginObserved"/>
    /// is the ACE login seam's entry point (called from every successful world login), so it must
    /// follow the same opt-in no-op pattern every other Cloud Mule seam here does when AC Cloud Mule
    /// is disabled -- ordinary login on a server that has never configured Cloud Mule must never even
    /// attempt to reach a Cloud database.
    /// </summary>
    [TestMethod]
    public void PublishCharacterLoginObserved_WhenCloudMuleDisabled_DoesNotThrow()
    {
        ConfigManager.Initialize(new MasterConfiguration());

        CloudIdentityEventManager.PublishCharacterLoginObserved(
            characterId: 0x80000001, accountId: 1, characterName: "Idle", totalLogins: 1, monarchId: null);
    }

    /// <summary>
    /// Mirrors <see cref="CheckMonarchDeletion_WhenCloudMuleEnabledButTheDatabaseIsUnreachable_FailsSafeByBlocking"/>:
    /// unlike the monarch-deletion guard, a failed publish has nothing to fail safe *to* (there is no
    /// decision to block) -- it must log and swallow the failure so an unreachable Cloud database never
    /// prevents an otherwise-successful world login.
    /// </summary>
    [TestMethod]
    public void PublishCharacterLoginObserved_WhenCloudMuleEnabledButTheDatabaseIsUnreachable_DoesNotThrow()
    {
        var configuration = new MasterConfiguration();
        configuration.CloudMule.Enabled = true;
        configuration.CloudMule.ShardId = "us1";
        configuration.MySql.Cloud.Host = "127.0.0.1";
        configuration.MySql.Cloud.Port = 1; // nothing listens here
        configuration.MySql.Cloud.Database = "ace_cloud";
        configuration.MySql.Cloud.Username = "root";
        configuration.MySql.Cloud.Password = "root";
        ConfigManager.Initialize(configuration);

        CloudIdentityEventManager.PublishCharacterLoginObserved(
            characterId: 0x80000001, accountId: 1, characterName: "Idle", totalLogins: 1, monarchId: 0x80000002);
    }
}
