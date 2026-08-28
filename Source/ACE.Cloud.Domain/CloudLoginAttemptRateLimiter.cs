using System.Threading;

namespace ACE.Cloud.Domain;

public enum CloudRateLimitOutcomeKind
{
    Allowed,
    RateLimited,
}

public sealed record CloudRateLimitResult(CloudRateLimitOutcomeKind Kind, TimeSpan? RetryAfter)
{
    public bool IsAllowed => Kind == CloudRateLimitOutcomeKind.Allowed;

    public static CloudRateLimitResult Allowed() => new(CloudRateLimitOutcomeKind.Allowed, RetryAfter: null);

    public static CloudRateLimitResult RateLimited(TimeSpan retryAfter) => new(CloudRateLimitOutcomeKind.RateLimited, retryAfter);
}

/// <summary>
/// A thread-safe fixed-window rate limiter keyed by an arbitrary string (security baseline:
/// "Rate-limit login, account linking, token creation/redemption..."). Each key tracks its own
/// independent window, so one attacked account name or source IP cannot exhaust another caller's
/// allowance. Callers supply <paramref name="nowUtc"/> explicitly rather than this type reading the
/// system clock, so tests can deterministically advance time. Guarded by a single lock rather than a
/// lock-free CAS loop: login-shaped traffic never makes this a contention hotspot, and a lock makes
/// "was this specific call the one that tipped into rate-limited" trivially correct instead of
/// racy.
///
/// The tracked-key count is capped at <see cref="MaxTrackedKeys"/>: since a caller supplies the key
/// (an account name or source IP from a public-facing endpoint), an unbounded map would itself be a
/// memory-exhaustion vector -- an attacker sending many requests for distinct fake account names
/// could otherwise grow this dictionary without limit. Once at capacity, inserting a new key first
/// sweeps already-expired windows (usually enough by itself, since windows are short-lived) and, if
/// still full, evicts the single oldest window to make room; that eviction can let one already
/// in-progress attacker key exceed its limit slightly early, which is an acceptable trade-off for a
/// bounded footprint.
/// </summary>
public sealed class CloudLoginAttemptRateLimiter
{
    private const int MaxTrackedKeys = 50_000;

    private readonly int _maxAttempts;
    private readonly TimeSpan _window;
    private readonly Dictionary<string, Window> _windowsByKey = new(StringComparer.Ordinal);
    private readonly Lock _sync = new();

    public CloudLoginAttemptRateLimiter(int maxAttempts, TimeSpan window)
    {
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "maxAttempts must be greater than 0.");
        }

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "window must be greater than TimeSpan.Zero.");
        }

        _maxAttempts = maxAttempts;
        _window = window;
    }

    public CloudRateLimitResult RegisterAttempt(string key, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A rate-limit key is required.", nameof(key));
        }

        lock (_sync)
        {
            if (!_windowsByKey.TryGetValue(key, out var current) || nowUtc >= current.StartedAtUtc + _window)
            {
                MakeRoomForNewKeyIfNeeded(nowUtc);
                _windowsByKey[key] = new Window(nowUtc, 1);
                return CloudRateLimitResult.Allowed();
            }

            if (current.Count >= _maxAttempts)
            {
                return CloudRateLimitResult.RateLimited(current.StartedAtUtc + _window - nowUtc);
            }

            _windowsByKey[key] = current with { Count = current.Count + 1 };
            return CloudRateLimitResult.Allowed();
        }
    }

    private void MakeRoomForNewKeyIfNeeded(DateTime nowUtc)
    {
        if (_windowsByKey.Count < MaxTrackedKeys)
        {
            return;
        }

        foreach (var expiredKey in _windowsByKey.Where(pair => nowUtc >= pair.Value.StartedAtUtc + _window).Select(pair => pair.Key).ToList())
        {
            _windowsByKey.Remove(expiredKey);
        }

        if (_windowsByKey.Count < MaxTrackedKeys)
        {
            return;
        }

        var oldestKey = _windowsByKey.MinBy(pair => pair.Value.StartedAtUtc).Key;
        _windowsByKey.Remove(oldestKey);
    }

    private sealed record Window(DateTime StartedAtUtc, int Count);
}
