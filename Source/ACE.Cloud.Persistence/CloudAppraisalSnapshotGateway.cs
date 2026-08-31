using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Read access to one biota's <see cref="CloudAppraisalSnapshotProjection"/> row (issue #34: the
/// complete rebuildable appraisal snapshot a Full Cloud Appraisal panel is built from). Interface-
/// extracted so <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake for endpoint tests,
/// matching <see cref="ICloudInventoryItemPropertiesGateway"/>.
/// </summary>
public interface ICloudAppraisalSnapshotGateway
{
    Task<CloudAppraisalRawItemSnapshot?> TryGetAsync(uint biotaId, string shardId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Idempotent upsert access to <see cref="CloudAppraisalSnapshotProjection"/>. Kept as its own narrow
/// gateway, separate from <see cref="CloudCustodyBoundary"/> and <see cref="CloudCustodyProjectionConsumer"/>,
/// for the same reason <see cref="CloudInventoryItemPropertiesGateway"/> is: writing an item's
/// rebuildable appraisal snapshot is neither a custody state transition nor an outbox-ordered event.
/// </summary>
public sealed class CloudAppraisalSnapshotGateway : ICloudAppraisalSnapshotGateway
{
    private readonly CloudDbContext _context;

    public CloudAppraisalSnapshotGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> UpsertAsync(
        uint biotaId,
        string shardId,
        CloudAppraisalRawItemSnapshot snapshot,
        long revision,
        CancellationToken cancellationToken = default)
    {
        var current = await _context.CloudAppraisalSnapshotProjections
            .SingleOrDefaultAsync(row => row.BiotaId == biotaId, cancellationToken);

        var (row, applied) = CloudAppraisalSnapshotProjection.TryApply(current, biotaId, shardId, snapshot, revision);

        if (!applied)
        {
            return false;
        }

        if (current is null)
        {
            _context.CloudAppraisalSnapshotProjections.Add(row);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CloudAppraisalRawItemSnapshot?> TryGetAsync(
        uint biotaId, string shardId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Reading an appraisal snapshot row requires a Cloud Shard ID.", nameof(shardId));
        }

        var row = await _context.CloudAppraisalSnapshotProjections
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.BiotaId == biotaId && r.ShardId == shardId, cancellationToken);

        return row?.ToSnapshot();
    }
}
