namespace ACE.Cloud.Domain;

/// <summary>
/// Composes authorization scoping (<see cref="CloudLiveStreamAuthorizationPolicy"/>'s "owner or
/// revalidated admin" rule, reused rather than duplicated -- see <see cref="CloudLiveStreamViewer"/>'s
/// doc comment on why the caller, not this type, is responsible for resolving a Sharing Grant into an
/// authorized owner ID), category filtering, deterministic sorting
/// (<see cref="CloudInventoryItemOrderPolicy"/>), and Mule Page pagination
/// (<see cref="CloudMulePagePolicy"/>) into the one query issue #30's Green section asks for: "one
/// filter/sort/page contract for grid and spreadsheet clients." This type is pure and storage-agnostic
/// -- <see cref="ACE.Cloud.Persistence.CloudInventoryQueryReader"/> is the real, authorization-scoped
/// database-backed caller (ARCH-012: scoping happens in the database query itself, this type composes
/// what the persistence layer already fetched, it never fetches unauthorized rows in order to filter
/// them out afterward).
/// </summary>
public static class CloudInventoryQueryEngine
{
    public static CloudInventoryQueryPageResult Query(
        IEnumerable<CloudInventoryQueryCandidate> authorizedCandidates,
        CloudInventoryCategory? category,
        int pageNumber,
        CloudInventorySortKey sortKey,
        CloudInventorySortDirection sortDirection)
    {
        ArgumentNullException.ThrowIfNull(authorizedCandidates);

        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "A Mule Page number must be positive.");
        }

        var scoped = category is null
            ? authorizedCandidates
            : authorizedCandidates.Where(candidate => candidate.Category == category.Value);

        var sorted = CloudInventoryItemOrderPolicy.Sort(scoped, sortKey, sortDirection);
        var totalItemsInScope = sorted.Count;
        var totalPages = CloudMulePagePolicy.GetPageCount(totalItemsInScope);
        var pageExists = CloudMulePagePolicy.PageExists(pageNumber, totalItemsInScope);

        var items = pageExists
            ? sorted
                .Skip((pageNumber - 1) * CloudMulePagePolicy.PageSize)
                .Take(CloudMulePagePolicy.PageSize)
                .Select(ToResultItem)
                .ToList()
            : [];

        var pageName = category is null ? null : CloudMulePagePolicy.FormatPageName(category.Value, pageNumber);

        return new CloudInventoryQueryPageResult(category, pageName, pageNumber, pageExists, totalItemsInScope, totalPages, items);
    }

    /// <summary>
    /// Filters <paramref name="candidates"/> down to what <paramref name="viewer"/> is authorized to
    /// see, reusing <see cref="CloudLiveStreamAuthorizationPolicy"/>'s exact private-scope rule
    /// (issue #30 Red: "owner, Sharing Grant, admin, revoked access") -- there is no separate
    /// inventory-specific authorization rule to drift from the Live State Stream's.
    /// </summary>
    public static IEnumerable<CloudInventoryQueryCandidate> Authorize(
        IEnumerable<CloudInventoryQueryCandidate> candidates, CloudLiveStreamViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(viewer);

        return candidates.Where(candidate =>
            CloudLiveStreamAuthorizationPolicy.IsVisibleTo(isPublic: false, candidate.OwnerId, viewer));
    }

    /// <summary>
    /// <paramref name="candidate"/> is already known to be visible to the current viewer
    /// (<see cref="Authorize"/> ran first); <c>canMutate: true</c> here reflects only the reservation
    /// state modeled today. SHARE-002/SHARE-003's View Only Sharing Grant tier (which must see an
    /// item but never be offered a mutation entry point) is not modeled by
    /// <see cref="CloudLiveStreamViewer"/> yet -- it carries only "which owners can this viewer see,"
    /// not a per-owner permission level -- so this issue deliberately never narrows
    /// <see cref="CloudInventoryPermittedActions"/> below what reservation state already implies. The
    /// Sharing Grant issue that introduces permission levels must further restrict these flags for a
    /// View Only viewer; it must never need to widen them.
    /// </summary>
    private static CloudInventoryQueryResultItem ToResultItem(CloudInventoryQueryCandidate candidate) =>
        new(
            candidate.ItemId,
            candidate.StackLotId,
            candidate.Name,
            candidate.Category,
            candidate.Quantity,
            candidate.Value,
            candidate.Burden,
            candidate.IsReserved,
            candidate.Version,
            CloudInventoryPermittedActions.For(candidate.IsReserved, canMutate: true),
            candidate.IconCacheKeyHex);
}
