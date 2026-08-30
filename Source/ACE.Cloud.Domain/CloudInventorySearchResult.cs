namespace ACE.Cloud.Domain;

/// <summary>
/// The result of one <see cref="CloudInventorySearchEngine.Search"/> call: either a completed,
/// deterministically sorted and paged result (<see cref="CloudSafeRegexSearchOutcomeKind.Matched"/>,
/// used for every plain text/property search and every successful Safe Regex Search) or one stable,
/// actionable non-completion outcome shared with <see cref="CloudSafeRegexSearchResult"/> (issue #32
/// acceptance: "Every resource limit returns a stable actionable result").
/// </summary>
public sealed record CloudInventorySearchResult
{
    public CloudSafeRegexSearchOutcomeKind Kind { get; }

    /// <summary>Populated only when <see cref="Kind"/> is <see cref="CloudSafeRegexSearchOutcomeKind.Matched"/>.</summary>
    public CloudInventoryQueryPageResult? Page { get; }

    /// <summary>A caller-displayable explanation, populated for every non-<see cref="CloudSafeRegexSearchOutcomeKind.Matched"/> kind.</summary>
    public string? Reason { get; }

    private CloudInventorySearchResult(CloudSafeRegexSearchOutcomeKind kind, CloudInventoryQueryPageResult? page, string? reason)
    {
        Kind = kind;
        Page = page;
        Reason = reason;
    }

    public static CloudInventorySearchResult Completed(CloudInventoryQueryPageResult page) =>
        new(CloudSafeRegexSearchOutcomeKind.Matched, page ?? throw new ArgumentNullException(nameof(page)), reason: null);

    public static CloudInventorySearchResult RateLimited() =>
        new(CloudSafeRegexSearchOutcomeKind.RateLimited, page: null, "Too many searches; please wait before trying again.");

    public static CloudInventorySearchResult FromRegexFailure(CloudSafeRegexSearchResult regexFailure)
    {
        ArgumentNullException.ThrowIfNull(regexFailure);

        if (regexFailure.Kind == CloudSafeRegexSearchOutcomeKind.Matched)
        {
            throw new ArgumentException("A regex failure result cannot itself be Matched.", nameof(regexFailure));
        }

        return new CloudInventorySearchResult(regexFailure.Kind, page: null, regexFailure.Reason);
    }
}
