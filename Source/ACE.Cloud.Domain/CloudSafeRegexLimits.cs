namespace ACE.Cloud.Domain;

/// <summary>
/// The hard resource budgets that bound <see cref="CloudSafeRegexEngine"/> independently of ordinary
/// text/property search (CONTEXT.md's <c>Safe Regex Search</c>: "constrained by non-backtracking
/// execution, time, pattern, input, result, and request-rate limits"). These are conservative
/// defaults, not administrator-tunable settings -- SRCH-001 only asks that an administrator be able
/// to disable regex mode entirely (<see cref="CloudSearchConfiguration"/>), not retune its budgets.
/// </summary>
public static class CloudSafeRegexLimits
{
    /// <summary>The longest pattern source text Safe Regex Search will attempt to compile.</summary>
    public const int MaxPatternLength = 200;

    /// <summary>
    /// The longest candidate name Safe Regex Search will match against, matching
    /// <c>CloudInventoryItemPropertiesProjection.Name</c>'s <c>VARCHAR(256)</c> column. Defensive
    /// only: every stored name already respects this bound.
    /// </summary>
    public const int MaxCandidateNameLength = 256;

    /// <summary>
    /// The largest already-authorized, already-filtered candidate set Safe Regex Search will scan.
    /// Exceeding this bounds regex mode independently of ordinary search, which has no such cap: a
    /// caller narrows with category/text/property filters first, rather than the engine silently
    /// scanning an unbounded private inventory.
    /// </summary>
    public const int MaxCandidatesToScan = 5_000;

    /// <summary>The most matches one Safe Regex Search call returns.</summary>
    public const int MaxResults = 200;

    /// <summary>
    /// The per-candidate match budget passed to <see cref="System.Text.RegularExpressions.Regex"/> as
    /// defense in depth. <see cref="System.Text.RegularExpressions.RegexOptions.NonBacktracking"/>
    /// already guarantees linear-time execution with no catastrophic backtracking, so this should
    /// never trip in practice; it exists so a still-unexpectedly-expensive match releases the caller
    /// promptly instead of blocking indefinitely.
    /// </summary>
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(50);
}
