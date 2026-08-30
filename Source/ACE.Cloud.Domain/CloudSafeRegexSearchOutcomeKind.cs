namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of one <see cref="CloudSafeRegexEngine.Search"/>/<see cref="CloudInventorySearchEngine.Search"/>
/// attempt (CONTEXT.md's <c>Safe Regex Search</c>: "constrained by non-backtracking execution, time,
/// pattern, input, result, and request-rate limits"). Every non-<see cref="Matched"/> value is a
/// stable, actionable result rather than a thrown exception or a hang (issue #32 acceptance: "Every
/// resource limit returns a stable actionable result and releases work promptly").
/// </summary>
public enum CloudSafeRegexSearchOutcomeKind
{
    /// <summary>The pattern compiled and evaluated within every budget; results are attached.</summary>
    Matched,

    /// <summary>An administrator has disabled Safe Regex Search independently of ordinary search.</summary>
    Disabled,

    /// <summary>The caller is currently rate-limited (security baseline: "Rate-limit ... search/regex").</summary>
    RateLimited,

    /// <summary><see cref="CloudSafeRegexLimits.MaxPatternLength"/> was exceeded.</summary>
    PatternTooLong,

    /// <summary>
    /// The pattern is syntactically well-formed but uses a construct <see cref="CloudSafeRegexLimits"/>'s
    /// non-backtracking engine cannot execute (for example a backreference or lookaround).
    /// </summary>
    UnsupportedPattern,

    /// <summary>The pattern is not a well-formed regular expression.</summary>
    InvalidPattern,

    /// <summary>
    /// The already-authorized, already-indexed-and-filtered candidate set is larger than
    /// <see cref="CloudSafeRegexLimits.MaxCandidatesToScan"/>; the caller must narrow with ordinary
    /// filters (category, text, property bounds) before Safe Regex Search may run.
    /// </summary>
    TooManyCandidates,

    /// <summary>
    /// A per-candidate match exceeded <see cref="CloudSafeRegexLimits.MatchTimeout"/>. The whole
    /// search is abandoned rather than returning a partial result, so a caller never mistakes a
    /// truncated scan for a complete one.
    /// </summary>
    TimedOut,
}
