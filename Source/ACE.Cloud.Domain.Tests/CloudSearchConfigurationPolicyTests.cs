namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green tests for issue #32's SRCH-001 admin-disablement half: "let admins disable regex
/// without degrading normal search," matching the revalidate-then-transition shape already proven by
/// <see cref="CloudMarketplaceStatePolicyTests"/>.
/// </summary>
[TestClass]
public sealed class CloudSearchConfigurationPolicyTests
{
    private const uint AdminAccessLevel = 5;
    private const uint NonAdminAccessLevel = 1;

    [TestMethod]
    public void Default_RegexSearchEnabled()
    {
        var current = CloudSearchConfiguration.Default();

        Assert.IsTrue(current.RegexSearchEnabled);
    }

    [TestMethod]
    public void SetRegexSearchEnabled_ByAdmin_ToDisabled_Succeeds_AndBumpsVersion()
    {
        var current = CloudSearchConfiguration.Default();

        var result = CloudSearchConfigurationPolicy.SetRegexSearchEnabled(current, requested: false, AdminAccessLevel);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.Configuration!.RegexSearchEnabled);
        Assert.AreEqual(current.Version.Next(), result.Configuration.Version);
    }

    [TestMethod]
    public void SetRegexSearchEnabled_ToTheSameValue_IsANoOp_AndDoesNotBumpVersion()
    {
        var current = CloudSearchConfiguration.Default();

        var result = CloudSearchConfigurationPolicy.SetRegexSearchEnabled(current, requested: true, AdminAccessLevel);

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual(current.Version, result.Configuration!.Version);
    }

    [TestMethod]
    public void SetRegexSearchEnabled_ByNonAdmin_IsRejected()
    {
        var current = CloudSearchConfiguration.Default();

        var result = CloudSearchConfigurationPolicy.SetRegexSearchEnabled(current, requested: false, NonAdminAccessLevel);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(current.RegexSearchEnabled, "A rejected change must not mutate the caller's current configuration.");
    }
}
