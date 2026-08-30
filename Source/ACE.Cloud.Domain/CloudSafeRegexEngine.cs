using System.Text.RegularExpressions;

namespace ACE.Cloud.Domain;

/// <summary>
/// CONTEXT.md's <c>Safe Regex Search</c>: "an advanced indexed-search mode constrained by
/// non-backtracking execution, time, pattern, input, result, and request-rate limits." This type
/// only performs the non-backtracking evaluation and its pattern/candidate/result budgets;
/// authorization scoping, category/text/property narrowing, admin disablement, and rate limiting all
/// happen in the caller (<see cref="CloudInventorySearchEngine"/>) before <paramref name="pattern"/>
/// ever reaches here, so this engine never sees a candidate the caller was not already authorized and
/// prepared to show.
///
/// Uses <see cref="RegexOptions.NonBacktracking"/> (SRCH-001: "non-backtracking execution"), which
/// guarantees linear-time matching with no catastrophic-backtracking blowup and, as a direct
/// consequence, rejects backreferences, lookahead, and lookbehind at construction time -- exactly the
/// "unsupported constructs" issue #32's Red section asks to test, and exactly why this engine never
/// needs a construct allowlist of its own. Regex evaluation is pure in-memory string matching against
/// already-fetched candidate names; it is never translated into or concatenated as SQL (acceptance:
/// "Regex never executes as SQL").
/// </summary>
public static class CloudSafeRegexEngine
{
    public static CloudSafeRegexSearchResult Search(
        IReadOnlyList<CloudInventoryQueryCandidate> narrowedCandidates,
        string pattern,
        bool regexSearchEnabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(narrowedCandidates);

        if (string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException("A Safe Regex Search pattern is required.", nameof(pattern));
        }

        if (!regexSearchEnabled)
        {
            return CloudSafeRegexSearchResult.Disabled();
        }

        if (pattern.Length > CloudSafeRegexLimits.MaxPatternLength)
        {
            return CloudSafeRegexSearchResult.PatternTooLong();
        }

        if (narrowedCandidates.Count > CloudSafeRegexLimits.MaxCandidatesToScan)
        {
            return CloudSafeRegexSearchResult.TooManyCandidates();
        }

        Regex regex;
        try
        {
            regex = new Regex(
                pattern,
                RegexOptions.NonBacktracking | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
                CloudSafeRegexLimits.MatchTimeout);
        }
        catch (NotSupportedException)
        {
            // RegexOptions.NonBacktracking rejects backreferences, lookahead, lookbehind, and other
            // constructs it cannot execute without backtracking at construction time, before any
            // candidate is ever scanned.
            return CloudSafeRegexSearchResult.UnsupportedPattern();
        }
        catch (RegexParseException)
        {
            return CloudSafeRegexSearchResult.InvalidPattern();
        }

        var matches = new List<CloudInventoryQueryCandidate>();

        foreach (var candidate in narrowedCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = candidate.Name.Length > CloudSafeRegexLimits.MaxCandidateNameLength
                ? candidate.Name[..CloudSafeRegexLimits.MaxCandidateNameLength]
                : candidate.Name;

            bool isMatch;
            try
            {
                isMatch = regex.IsMatch(name);
            }
            catch (RegexMatchTimeoutException)
            {
                // Abandon the whole search rather than return a partial scan the caller could mistake
                // for a complete one (issue #32 acceptance: "releases work promptly").
                return CloudSafeRegexSearchResult.TimedOut();
            }

            if (!isMatch)
            {
                continue;
            }

            matches.Add(candidate);
            if (matches.Count >= CloudSafeRegexLimits.MaxResults)
            {
                break;
            }
        }

        return CloudSafeRegexSearchResult.Matched(matches);
    }
}
