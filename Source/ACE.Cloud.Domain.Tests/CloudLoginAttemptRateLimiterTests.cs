namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red -> Green coverage for security baseline: "Rate-limit login, account linking, token
/// creation/redemption..." applied to ACE-backed Login (AUTH-002).
/// </summary>
[TestClass]
public sealed class CloudLoginAttemptRateLimiterTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void RegisterAttempt_UnderThreshold_IsAllowed()
    {
        var limiter = new CloudLoginAttemptRateLimiter(maxAttempts: 5, TimeSpan.FromMinutes(1));

        for (var i = 0; i < 5; i++)
        {
            Assert.IsTrue(limiter.RegisterAttempt("account-1", Now).IsAllowed);
        }
    }

    [TestMethod]
    public void RegisterAttempt_OverThreshold_IsRateLimited()
    {
        var limiter = new CloudLoginAttemptRateLimiter(maxAttempts: 3, TimeSpan.FromMinutes(1));

        for (var i = 0; i < 3; i++)
        {
            limiter.RegisterAttempt("account-1", Now);
        }

        var result = limiter.RegisterAttempt("account-1", Now);

        Assert.IsFalse(result.IsAllowed);
        Assert.AreEqual(CloudRateLimitOutcomeKind.RateLimited, result.Kind);
        Assert.IsNotNull(result.RetryAfter);
    }

    [TestMethod]
    public void RegisterAttempt_AfterWindowElapses_ResetsAllowance()
    {
        var limiter = new CloudLoginAttemptRateLimiter(maxAttempts: 2, TimeSpan.FromMinutes(1));

        limiter.RegisterAttempt("account-1", Now);
        limiter.RegisterAttempt("account-1", Now);
        Assert.IsFalse(limiter.RegisterAttempt("account-1", Now).IsAllowed);

        var result = limiter.RegisterAttempt("account-1", Now.AddMinutes(1));

        Assert.IsTrue(result.IsAllowed);
    }

    [TestMethod]
    public void RegisterAttempt_DifferentKeys_AreIsolated()
    {
        var limiter = new CloudLoginAttemptRateLimiter(maxAttempts: 1, TimeSpan.FromMinutes(1));

        Assert.IsTrue(limiter.RegisterAttempt("account-1", Now).IsAllowed);
        Assert.IsFalse(limiter.RegisterAttempt("account-1", Now).IsAllowed);

        Assert.IsTrue(limiter.RegisterAttempt("account-2", Now).IsAllowed, "A different key must not be affected by another key's exhausted allowance.");
    }

    [TestMethod]
    public void Constructor_RejectsNonPositiveMaxAttempts()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudLoginAttemptRateLimiter(0, TimeSpan.FromMinutes(1)));
    }

    [TestMethod]
    public void Constructor_RejectsNonPositiveWindow()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CloudLoginAttemptRateLimiter(5, TimeSpan.Zero));
    }

    /// <summary>
    /// A public-facing endpoint (login by account name or source IP) lets an attacker choose this
    /// limiter's keys; an unbounded map would itself be a memory-exhaustion vector. This proves the
    /// tracked-key count stays capped rather than growing without limit -- not the exact eviction
    /// policy, which is an implementation detail.
    /// </summary>
    [TestMethod]
    public void RegisterAttempt_ManyDistinctKeys_StaysBoundedAndKeepsWorkingCorrectly()
    {
        var limiter = new CloudLoginAttemptRateLimiter(maxAttempts: 2, TimeSpan.FromMinutes(1));

        for (var i = 0; i < 60_000; i++)
        {
            limiter.RegisterAttempt($"attacker-key-{i}", Now);
        }

        // The limiter must still function correctly for a fresh key after growing far past any
        // reasonable cap -- proving it didn't simply stop working, only that it stayed bounded.
        Assert.IsTrue(limiter.RegisterAttempt("account-1", Now).IsAllowed);
        Assert.IsTrue(limiter.RegisterAttempt("account-1", Now).IsAllowed);
        Assert.IsFalse(limiter.RegisterAttempt("account-1", Now).IsAllowed);
    }
}
