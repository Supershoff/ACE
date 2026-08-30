using ACE.Cloud.Domain;

namespace ACE.Cloud.Backend;

/// <summary>
/// A dedicated rate limiter instance for account-linking attempts (security baseline: "Rate-limit
/// login, account linking, token creation/redemption..."), distinct from the login endpoint's own
/// <see cref="CloudLoginAttemptRateLimiter"/> instance so the two limits track independently and
/// each type resolves unambiguously from dependency injection.
/// </summary>
public sealed class CloudAccountLinkAttemptRateLimiter
{
    private readonly CloudLoginAttemptRateLimiter _inner;

    public CloudAccountLinkAttemptRateLimiter(int maxAttempts, TimeSpan window)
    {
        _inner = new CloudLoginAttemptRateLimiter(maxAttempts, window);
    }

    public CloudRateLimitResult RegisterAttempt(string key, DateTime nowUtc) => _inner.RegisterAttempt(key, nowUtc);
}
