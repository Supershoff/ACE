using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Interface-extracted (mirroring <see cref="ICloudInventoryQueryReader"/>) so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake for endpoint tests instead of
/// standing up a real MariaDB-backed <see cref="CloudDbContext"/>.
/// </summary>
public interface ICloudActivityLedgerQueryReader
{
    Task<CloudActivityLedgerPage> QueryAsync(
        string shardId, CloudLiveStreamViewer viewer, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
}

/// <summary>
/// The scoped Activity Ledger query (issue #34, EVT-001/EVT-002). Owner/Shared/Vault scopes are all
/// the same query with different <see cref="CloudLiveStreamViewer.AuthorizedOwnerIds"/> compositions
/// (see <see cref="CloudActivityLedgerQueryEngine"/>'s doc comment) and are always restricted to the
/// owner-scoped <see cref="CloudActivityLedgerCategory.CustodyBoundary"/> table -- the three
/// admin-only ledger tables (account link, Global Cloud Maintenance, Asset Import) have no per-owner
/// Cloud identity to authorize against and are only ever queried for an admin viewer, matching
/// CONTEXT.md's "administrators may inspect the global ledger." Filtering by shard and by owner
/// always happens inside each database query itself (security baseline). A non-admin viewer's single-
/// table query pages and counts entirely at the database level, so its history is never truncated
/// regardless of how large it grows; the admin merge across four heterogeneous tables has no such
/// single-query option, so it still fetches a bounded newest-first per-table candidate window before
/// the pure <see cref="CloudActivityLedgerQueryEngine"/> composes/paginates the merged result (see
/// <see cref="QueryAsync"/>'s own comments for that window's bound).
/// </summary>
public sealed class CloudActivityLedgerQueryReader : ICloudActivityLedgerQueryReader
{
    /// <summary>
    /// How many of each admin-only table's newest rows are considered before merging and paginating,
    /// at minimum. Bounded rather than unlimited so a single admin ledger page request cannot force
    /// an unbounded table scan (security baseline: "unbounded work on public inputs" applies equally
    /// to an authenticated admin endpoint).
    /// </summary>
    private const int PerTableCandidateWindow = 500;

    /// <summary>
    /// The hard ceiling on <see cref="PerTableCandidateWindow"/>'s scaling: however deep an admin
    /// page request reaches, no single table is ever scanned past this many of its newest rows.
    /// </summary>
    private const int MaxPerTableCandidateWindow = 5_000;

    private readonly CloudDbContext _context;

    public CloudActivityLedgerQueryReader(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<CloudActivityLedgerPage> QueryAsync(
        string shardId, CloudLiveStreamViewer viewer, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("An Activity Ledger query requires a Cloud Shard ID.", nameof(shardId));
        }

        ArgumentNullException.ThrowIfNull(viewer);

        if (pageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "An Activity Ledger page number must be positive.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "An Activity Ledger page size must be positive.");
        }

        if (!viewer.IsAdmin)
        {
            // A non-admin viewer only ever queries the single owner-scoped CustodyBoundary table
            // (see this type's own doc comment), so this pages and counts at the database level
            // instead of the admin path's fetch-a-bounded-window-then-paginate-in-memory approach
            // below -- the only way this stays correct once a single owner accumulates more
            // CloudActivityLedgerEvent rows than any fixed candidate window could hold.
            return await QueryOwnerScopedCustodyBoundaryPageAsync(shardId, viewer.AuthorizedOwnerIds, pageNumber, pageSize, cancellationToken);
        }

        // The admin merge across four heterogeneous tables has no single SQL query that can
        // globally rank and page across all of them, so it still fetches a bounded newest-first
        // candidate window per table before composing/paginating in memory. That window is scaled
        // to the deepest page actually requested (capped by MaxPerTableCandidateWindow) so a deep
        // admin page request finds real rows instead of coming back empty; totalCount/totalPages
        // can still under-report a table whose true row count exceeds the resulting window.
        var candidateWindow = Math.Clamp(pageNumber * pageSize, PerTableCandidateWindow, MaxPerTableCandidateWindow);

        var candidates = new List<CloudActivityLedgerEntry>();
        candidates.AddRange(await ReadCustodyBoundaryCandidatesAsync(shardId, candidateWindow, cancellationToken));
        candidates.AddRange(await ReadAccountLinkCandidatesAsync(shardId, candidateWindow, cancellationToken));
        candidates.AddRange(await ReadGlobalMaintenanceCandidatesAsync(shardId, candidateWindow, cancellationToken));
        candidates.AddRange(await ReadAssetImportCandidatesAsync(shardId, candidateWindow, cancellationToken));
        candidates.AddRange(await ReadSharingGrantCandidatesAsync(shardId, candidateWindow, cancellationToken));

        var authorized = CloudActivityLedgerQueryEngine.Authorize(candidates, viewer);
        return CloudActivityLedgerQueryEngine.Paginate(authorized, pageNumber, pageSize);
    }

    private async Task<CloudActivityLedgerPage> QueryOwnerScopedCustodyBoundaryPageAsync(
        string shardId, IReadOnlySet<Guid> ownerIds, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        if (ownerIds.Count == 0)
        {
            return new CloudActivityLedgerPage([], pageNumber, pageSize, TotalCount: 0, TotalPages: 0);
        }

        var query = _context.CloudActivityLedgerEvents.AsNoTracking()
            .Where(evt => evt.ShardId == shardId && ownerIds.Contains(evt.OwnerId));

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (totalCount + pageSize - 1) / pageSize;

        var rows = await query
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .ThenByDescending(evt => evt.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var entries = rows.ConvertAll(evt => new CloudActivityLedgerEntry(
            evt.Id,
            evt.CorrelationId,
            evt.ShardId,
            CloudActivityLedgerCategory.CustodyBoundary,
            evt.EventType.ToString(),
            evt.OwnerId,
            evt.BiotaId,
            evt.Outcome.ToString(),
            evt.Reason,
            evt.OccurredAtUtc));

        return new CloudActivityLedgerPage(entries, pageNumber, pageSize, totalCount, totalPages);
    }

    private async Task<List<CloudActivityLedgerEntry>> ReadCustodyBoundaryCandidatesAsync(
        string shardId, int candidateWindow, CancellationToken cancellationToken)
    {
        var rows = await _context.CloudActivityLedgerEvents.AsNoTracking()
            .Where(evt => evt.ShardId == shardId)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(candidateWindow)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(evt => new CloudActivityLedgerEntry(
            evt.Id,
            evt.CorrelationId,
            evt.ShardId,
            CloudActivityLedgerCategory.CustodyBoundary,
            evt.EventType.ToString(),
            evt.OwnerId,
            evt.BiotaId,
            evt.Outcome.ToString(),
            evt.Reason,
            evt.OccurredAtUtc));
    }

    private async Task<List<CloudActivityLedgerEntry>> ReadAccountLinkCandidatesAsync(string shardId, int candidateWindow, CancellationToken cancellationToken)
    {
        var rows = await _context.CloudAccountLinkLedgerEvents.AsNoTracking()
            .Where(evt => evt.ShardId == shardId)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(candidateWindow)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(evt => new CloudActivityLedgerEntry(
            evt.Id,
            evt.CorrelationId,
            evt.ShardId,
            CloudActivityLedgerCategory.AccountLink,
            evt.EventType.ToString(),
            OwnerId: null,
            ItemBiotaId: null,
            Outcome: null,
            evt.Reason,
            evt.OccurredAtUtc));
    }

    private async Task<List<CloudActivityLedgerEntry>> ReadGlobalMaintenanceCandidatesAsync(string shardId, int candidateWindow, CancellationToken cancellationToken)
    {
        var rows = await _context.CloudGlobalMaintenanceLedgerEvents.AsNoTracking()
            .Where(evt => evt.ShardId == shardId)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(candidateWindow)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(evt => new CloudActivityLedgerEntry(
            evt.Id,
            evt.CorrelationId,
            evt.ShardId,
            CloudActivityLedgerCategory.GlobalMaintenance,
            evt.EventType.ToString(),
            OwnerId: null,
            ItemBiotaId: null,
            Outcome: null,
            evt.Reason,
            evt.OccurredAtUtc));
    }

    private async Task<List<CloudActivityLedgerEntry>> ReadAssetImportCandidatesAsync(string shardId, int candidateWindow, CancellationToken cancellationToken)
    {
        var rows = await _context.CloudAssetImportLedgerEvents.AsNoTracking()
            .Where(evt => evt.ShardId == shardId)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(candidateWindow)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(evt => new CloudActivityLedgerEntry(
            evt.Id,
            evt.CorrelationId,
            evt.ShardId,
            CloudActivityLedgerCategory.AssetImport,
            evt.EventType.ToString(),
            OwnerId: null,
            ItemBiotaId: null,
            Outcome: null,
            evt.Reason,
            evt.OccurredAtUtc));
    }

    private async Task<List<CloudActivityLedgerEntry>> ReadSharingGrantCandidatesAsync(string shardId, int candidateWindow, CancellationToken cancellationToken)
    {
        var rows = await _context.CloudSharingGrantLedgerEvents.AsNoTracking()
            .Where(evt => evt.ShardId == shardId)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(candidateWindow)
            .ToListAsync(cancellationToken);

        return rows.ConvertAll(evt => new CloudActivityLedgerEntry(
            evt.Id,
            evt.CorrelationId,
            evt.ShardId,
            CloudActivityLedgerCategory.SharingGrant,
            evt.EventType.ToString(),
            OwnerId: null,
            ItemBiotaId: null,
            Outcome: null,
            evt.Reason,
            evt.OccurredAtUtc));
    }
}
