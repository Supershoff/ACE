namespace ACE.Cloud.Domain;

/// <summary>
/// The pure scoping/ordering/pagination rule behind the scoped Activity Ledger query (issue #34's
/// Red: "owner/shared/admin/vault ledger scopes ... pagination"). Deliberately mirrors
/// <see cref="CloudInventoryQueryEngine"/>'s own split exactly: this type is storage-agnostic and
/// composes what a persistence-layer reader already fetched; it never decides which rows to fetch
/// from the database.
///
/// There is no separate "Shared" or "Vault" enum case here because both collapse into the same
/// mechanism <see cref="CloudLiveStreamViewer.AuthorizedOwnerIds"/> already uses for Owner scope: a
/// caller resolving a Vault-scoped view adds the viewer's current Allegiance Vault owner ID(s) to
/// that same set before calling <see cref="Authorize"/> (see
/// <c>ACE.Cloud.Persistence.CloudActivityLedgerQueryReader</c>'s doc comment), and a caller resolving
/// a Shared-scoped view will do the same once Sharing Grants (SHARE-001..004) exist to supply
/// grantor owner IDs -- there is deliberately no second authorization rule to drift from the Live
/// State Stream's.
/// </summary>
public static class CloudActivityLedgerQueryEngine
{
    /// <summary>
    /// Filters <paramref name="candidates"/> down to what <paramref name="viewer"/> is authorized to
    /// see. An admin sees every candidate, including the three admin-only categories
    /// (<see cref="CloudActivityLedgerEntry.OwnerId"/> null); a non-admin viewer sees only
    /// owner-scoped candidates whose <see cref="CloudActivityLedgerEntry.OwnerId"/> is in their
    /// authorized set -- an admin-only category candidate is never visible to a non-admin, matching
    /// CONTEXT.md's "users see ledger activity involving their assets or actions ... administrators
    /// may inspect the global ledger."
    /// </summary>
    public static IEnumerable<CloudActivityLedgerEntry> Authorize(
        IEnumerable<CloudActivityLedgerEntry> candidates, CloudLiveStreamViewer viewer)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(viewer);

        if (viewer.IsAdmin)
        {
            return candidates;
        }

        return candidates.Where(entry =>
            entry.OwnerId is { } ownerId && CloudLiveStreamAuthorizationPolicy.IsVisibleTo(isPublic: false, ownerId, viewer));
    }

    /// <summary>
    /// Orders already-authorized entries newest-first, with a deterministic Id tie-break for entries
    /// sharing the same <see cref="CloudActivityLedgerEntry.OccurredAtUtc"/> value, then paginates.
    /// </summary>
    public static CloudActivityLedgerPage Paginate(
        IEnumerable<CloudActivityLedgerEntry> authorizedEntries, int pageNumber, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(authorizedEntries);

        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "An Activity Ledger page number must be positive.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "An Activity Ledger page size must be positive.");
        }

        var ordered = authorizedEntries
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .ToList();

        var totalCount = ordered.Count;
        var totalPages = totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize;

        var pageEntries = ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new CloudActivityLedgerPage(pageEntries, pageNumber, pageSize, totalCount, totalPages);
    }
}
