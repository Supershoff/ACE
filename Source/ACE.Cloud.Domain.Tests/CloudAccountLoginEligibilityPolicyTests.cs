namespace ACE.Cloud.Domain.Tests;

/// <summary>Red -> Green coverage for issue #19's Red section: "...banned/disabled accounts..." (AUTH-002).</summary>
[TestClass]
public sealed class CloudAccountLoginEligibilityPolicyTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Evaluate_NoBan_IsEligible()
    {
        var account = new CloudAceAccountSnapshot(1, "player1", "hash", "use bcrypt", 0, BanExpireTime: null, BanReason: null);

        var result = CloudAccountLoginEligibilityPolicy.Evaluate(account, Now);

        Assert.IsTrue(result.IsEligible);
    }

    [TestMethod]
    public void Evaluate_ActiveBan_IsBanned()
    {
        var account = new CloudAceAccountSnapshot(
            1, "player1", "hash", "use bcrypt", 0, BanExpireTime: Now.AddDays(1), BanReason: "cheating");

        var result = CloudAccountLoginEligibilityPolicy.Evaluate(account, Now);

        Assert.IsFalse(result.IsEligible);
        Assert.AreEqual(CloudAccountLoginEligibilityKind.Banned, result.Kind);
        StringAssert.Contains(result.Reason, "cheating");
    }

    [TestMethod]
    public void Evaluate_ExpiredBan_IsEligible()
    {
        var account = new CloudAceAccountSnapshot(
            1, "player1", "hash", "use bcrypt", 0, BanExpireTime: Now.AddDays(-1), BanReason: "cheating");

        var result = CloudAccountLoginEligibilityPolicy.Evaluate(account, Now);

        Assert.IsTrue(result.IsEligible);
    }

    [TestMethod]
    public void Evaluate_BanExpiringExactlyNow_IsEligible()
    {
        var account = new CloudAceAccountSnapshot(1, "player1", "hash", "use bcrypt", 0, BanExpireTime: Now, BanReason: "cheating");

        var result = CloudAccountLoginEligibilityPolicy.Evaluate(account, Now);

        Assert.IsTrue(result.IsEligible, "A ban expiring exactly now must match ACE's own 'now < BanExpireTime' native check.");
    }

    [TestMethod]
    public void Evaluate_NoReason_UsesGenericMessage()
    {
        var account = new CloudAceAccountSnapshot(1, "player1", "hash", "use bcrypt", 0, BanExpireTime: Now.AddDays(1), BanReason: null);

        var result = CloudAccountLoginEligibilityPolicy.Evaluate(account, Now);

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Reason));
    }
}
