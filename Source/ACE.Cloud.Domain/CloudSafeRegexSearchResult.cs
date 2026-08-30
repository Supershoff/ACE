namespace ACE.Cloud.Domain;

/// <summary>
/// The result of one <see cref="CloudSafeRegexEngine.Search"/> attempt: either the matched candidates
/// (<see cref="CloudSafeRegexSearchOutcomeKind.Matched"/>) or one stable, actionable non-match outcome
/// (issue #32 acceptance: "Every resource limit returns a stable actionable result").
/// </summary>
public sealed record CloudSafeRegexSearchResult
{
    public CloudSafeRegexSearchOutcomeKind Kind { get; }

    /// <summary>Populated only when <see cref="Kind"/> is <see cref="CloudSafeRegexSearchOutcomeKind.Matched"/>.</summary>
    public IReadOnlyList<CloudInventoryQueryCandidate> Matches { get; }

    /// <summary>A caller-displayable explanation, populated for every non-<see cref="CloudSafeRegexSearchOutcomeKind.Matched"/> kind.</summary>
    public string? Reason { get; }

    private CloudSafeRegexSearchResult(CloudSafeRegexSearchOutcomeKind kind, IReadOnlyList<CloudInventoryQueryCandidate> matches, string? reason)
    {
        Kind = kind;
        Matches = matches;
        Reason = reason;
    }

    public static CloudSafeRegexSearchResult Matched(IReadOnlyList<CloudInventoryQueryCandidate> matches) =>
        new(CloudSafeRegexSearchOutcomeKind.Matched, matches ?? throw new ArgumentNullException(nameof(matches)), reason: null);

    public static CloudSafeRegexSearchResult Disabled() =>
        Failure(CloudSafeRegexSearchOutcomeKind.Disabled, "Safe Regex Search is currently disabled by an administrator.");

    public static CloudSafeRegexSearchResult RateLimited() =>
        Failure(CloudSafeRegexSearchOutcomeKind.RateLimited, "Too many searches; please wait before trying again.");

    public static CloudSafeRegexSearchResult PatternTooLong() =>
        Failure(CloudSafeRegexSearchOutcomeKind.PatternTooLong, $"A Safe Regex Search pattern cannot exceed {CloudSafeRegexLimits.MaxPatternLength} characters.");

    public static CloudSafeRegexSearchResult UnsupportedPattern() =>
        Failure(CloudSafeRegexSearchOutcomeKind.UnsupportedPattern, "This pattern uses a construct Safe Regex Search does not support (for example a backreference or lookaround).");

    public static CloudSafeRegexSearchResult InvalidPattern() =>
        Failure(CloudSafeRegexSearchOutcomeKind.InvalidPattern, "This is not a valid regular expression.");

    public static CloudSafeRegexSearchResult TooManyCandidates() =>
        Failure(CloudSafeRegexSearchOutcomeKind.TooManyCandidates, $"Narrow your search (category, text, or property filters) to at most {CloudSafeRegexLimits.MaxCandidatesToScan} items before using Safe Regex Search.");

    public static CloudSafeRegexSearchResult TimedOut() =>
        Failure(CloudSafeRegexSearchOutcomeKind.TimedOut, "This pattern took too long to evaluate.");

    private static CloudSafeRegexSearchResult Failure(CloudSafeRegexSearchOutcomeKind kind, string reason) =>
        new(kind, [], reason);
}
