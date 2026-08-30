namespace ACE.Cloud.Domain;

/// <summary>
/// Composes typed text/property filtering (SRCH-001: "normal and property search run against an
/// authorization-scoped prepared index") with an optional, explicitly opted-in Safe Regex Search pass
/// (<see cref="CloudSafeRegexEngine"/>, bounded independently of ordinary search per
/// <see cref="CloudSafeRegexLimits"/>) and the existing deterministic sort/page contract
/// (<see cref="CloudInventoryQueryEngine.Query"/>) into one typed search entry point. Pure and
/// storage-agnostic, exactly like <see cref="CloudInventoryQueryEngine"/>: <paramref name="authorizedCandidates"/>
/// must already be authorization-scoped by the caller (<see cref="ACE.Cloud.Persistence.CloudInventorySearchReader"/>
/// fetches only already-scoped rows from the database -- see <see cref="CloudInventoryQueryReader"/>'s
/// doc comment on why that scoping happens in the database query itself) -- this type never fetches
/// unauthorized rows in order to filter them out afterward, and it never builds or executes SQL of its
/// own (acceptance: "Regex never executes as SQL or scans unauthorized raw data").
///
/// Disabling Safe Regex Search never touches ordinary search: a request with no
/// <see cref="CloudInventorySearchFilter.RegexPattern"/> never calls <see cref="CloudSafeRegexEngine"/>
/// at all, so <paramref name="regexSearchEnabled"/> being false changes nothing about it (SRCH-001:
/// "let admins disable regex without degrading normal search").
/// </summary>
public static class CloudInventorySearchEngine
{
    public static CloudInventorySearchResult Search(
        IEnumerable<CloudInventoryQueryCandidate> authorizedCandidates,
        CloudInventorySearchFilter filter,
        bool regexSearchEnabled,
        CloudRateLimitResult rateLimitResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorizedCandidates);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(rateLimitResult);

        if (filter.Page <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(filter), "A Mule Page number must be positive.");
        }

        if (!rateLimitResult.IsAllowed)
        {
            return CloudInventorySearchResult.RateLimited();
        }

        var filtered = ApplyTextAndPropertyFilters(authorizedCandidates, filter);

        if (!string.IsNullOrEmpty(filter.RegexPattern))
        {
            var regexResult = CloudSafeRegexEngine.Search(filtered, filter.RegexPattern, regexSearchEnabled, cancellationToken);
            if (regexResult.Kind != CloudSafeRegexSearchOutcomeKind.Matched)
            {
                return CloudInventorySearchResult.FromRegexFailure(regexResult);
            }

            filtered = regexResult.Matches;
        }

        var page = CloudInventoryQueryEngine.Query(filtered, filter.Category, filter.Page, filter.SortKey, filter.SortDirection);
        return CloudInventorySearchResult.Completed(page);
    }

    private static IReadOnlyList<CloudInventoryQueryCandidate> ApplyTextAndPropertyFilters(
        IEnumerable<CloudInventoryQueryCandidate> candidates, CloudInventorySearchFilter filter)
    {
        var query = candidates;

        if (filter.Category is not null)
        {
            query = query.Where(candidate => candidate.Category == filter.Category.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.NameContains))
        {
            query = query.Where(candidate => candidate.Name.Contains(filter.NameContains, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.MinValue is not null)
        {
            query = query.Where(candidate => candidate.Value is not null && candidate.Value >= filter.MinValue);
        }

        if (filter.MaxValue is not null)
        {
            query = query.Where(candidate => candidate.Value is not null && candidate.Value <= filter.MaxValue);
        }

        if (filter.MinBurden is not null)
        {
            query = query.Where(candidate => candidate.Burden is not null && candidate.Burden >= filter.MinBurden);
        }

        if (filter.MaxBurden is not null)
        {
            query = query.Where(candidate => candidate.Burden is not null && candidate.Burden <= filter.MaxBurden);
        }

        if (filter.MinQuantity is not null)
        {
            query = query.Where(candidate => candidate.Quantity >= filter.MinQuantity);
        }

        if (filter.MaxQuantity is not null)
        {
            query = query.Where(candidate => candidate.Quantity <= filter.MaxQuantity);
        }

        return query.ToList();
    }
}
