using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Read access to one biota's <see cref="CloudIconCompositionInputsProjection"/> row. A runtime icon
/// composition worker (<c>ACE.Cloud.Worker</c>) reads this to compose a missing/stale icon without
/// ever needing ACE.Server's live WorldObject or direct ace_shard access (ARCH-002/ARCH-004).
/// </summary>
public interface ICloudIconCompositionInputsGateway
{
    Task<CloudIconCompositionInputs?> TryGetAsync(uint biotaId, string shardId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Idempotent upsert access to <see cref="CloudIconCompositionInputsProjection"/>. Kept as its own
/// narrow gateway for the same reason <see cref="CloudInventoryItemPropertiesGateway"/> is: capturing
/// an item's icon composition inputs is neither a custody state transition nor an outbox-ordered event.
/// </summary>
public sealed class CloudIconCompositionInputsGateway : ICloudIconCompositionInputsGateway
{
    private readonly CloudDbContext _context;

    public CloudIconCompositionInputsGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> UpsertAsync(
        uint biotaId,
        string shardId,
        CloudIconCompositionInputs inputs,
        long revision,
        CancellationToken cancellationToken = default)
    {
        var current = await _context.CloudIconCompositionInputsProjections
            .SingleOrDefaultAsync(row => row.BiotaId == biotaId, cancellationToken);

        var (row, applied) = CloudIconCompositionInputsProjection.TryApply(current, biotaId, shardId, inputs, revision);

        if (!applied)
        {
            return false;
        }

        if (current is null)
        {
            _context.CloudIconCompositionInputsProjections.Add(row);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CloudIconCompositionInputs?> TryGetAsync(
        uint biotaId, string shardId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Reading an icon composition inputs row requires a Cloud Shard ID.", nameof(shardId));
        }

        var row = await _context.CloudIconCompositionInputsProjections
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.BiotaId == biotaId && r.ShardId == shardId, cancellationToken);

        return row?.ToInputs();
    }
}
