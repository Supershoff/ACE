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
/// always happens inside each database query itself (security baseline), and every table's rows are
/// fetched newest-first with a bounded per-table candidate window before the pure
/// <see cref="CloudActivityLedgerQueryEngine"/> composes/paginates the merged result, so an admin's
/// deep page request never has to load a whole table into memory to find it.
/// </summary>
public sealed class CloudActivityLedgerQueryReader : ICloudActivityLedgerQueryReader
{
    /// <summary>
    /// How many of each admin-only table's newest rows are considered before merging and paginating.
    /// Bounded rather than unlimited so a single admin ledger page request cannot force an unbounded
    /// table scan (security baseline: "unbounded work on public inputs" applies equally to an
    /// authenticated admin endpoint).
    /// </summary>
    private const int PerTableCandidateWindow = 500;

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

        var candidates = new List<CloudActivityLedgerEntry>();

        if (viewer.IsAdmin)
        {
            candidates.AddRange(await ReadCustodyBoundaryCandidatesAsync(shardId, ownerIds: null, cancellationToken));
            candidates.AddRange(await ReadAccountLinkCandidatesAsync(shardId, cancellationToken));
            candidates.AddRange(await ReadGlobalMaintenanceCandidatesAsync(shardId, cancellationToken));
            candidates.AddRange(await ReadAssetImportCandidatesAsync(shardId, cancellationToken));
        }
        else
        {
            candidates.AddRange(await ReadCustodyBoundaryCandidatesAsync(shardId, viewer.AuthorizedOwnerIds, cancellationToken));
        }

        var authorized = CloudActivityLedgerQueryEngine.Authorize(candidates, viewer);
        return CloudActivityLedgerQueryEngine.Paginate(authorized, pageNumber, pageSize);
    }

    private async Task<List<CloudActivityLedgerEntry>> ReadCustodyBoundaryCandidatesAsync(
        string shardId, IReadOnlySet<Guid>? ownerIds, CancellationToken cancellationToken)
    {
        var query = _context.CloudActivityLedgerEvents.AsNoTracking().Where(evt => evt.ShardId == shardId);
        if (ownerIds is not null)
        {
            if (ownerIds.Count == 0)
            {
                return [];
            }

            query = query.Where(evt => ownerIds.Contains(evt.OwnerId));
        }

        var rows = await query
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(PerTableCandidateWindow)
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

    private async Task<List<CloudActivityLedgerEntry>> ReadAccountLinkCandidatesAsync(string shardId, CancellationToken cancellationToken)
    {
        var rows = await _context.CloudAccountLinkLedgerEvents.AsNoTracking()
            .Where(evt => evt.ShardId == shardId)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(PerTableCandidateWindow)
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

    private async Task<List<CloudActivityLedgerEntry>> ReadGlobalMaintenanceCandidatesAsync(string shardId, CancellationToken cancellationToken)
    {
        var rows = await _context.CloudGlobalMaintenanceLedgerEvents.AsNoTracking()
            .Where(evt => evt.ShardId == shardId)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(PerTableCandidateWindow)
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

    private async Task<List<CloudActivityLedgerEntry>> ReadAssetImportCandidatesAsync(string shardId, CancellationToken cancellationToken)
    {
        var rows = await _context.CloudAssetImportLedgerEvents.AsNoTracking()
            .Where(evt => evt.ShardId == shardId)
            .OrderByDescending(evt => evt.OccurredAtUtc)
            .Take(PerTableCandidateWindow)
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
}
