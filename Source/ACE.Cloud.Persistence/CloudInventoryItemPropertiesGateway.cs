using ACE.Entity.Enum;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Idempotent upsert access to <see cref="CloudInventoryItemPropertiesProjection"/> (issue #30
/// Green). Kept as its own narrow gateway, separate from <see cref="CloudCustodyBoundary"/> (ACE's
/// World Boundary Authority gateway) and from <see cref="CloudCustodyProjectionConsumer"/> (the
/// Custody Outbox consumer): writing an item's display properties is neither a custody state
/// transition nor an outbox-ordered event, so it does not belong on either of those surfaces (see
/// <c>CloudWorldBoundaryAuthoritySurfaceTests</c>, which would fail this build if a Cloud-only
/// concept leaked onto <see cref="CloudCustodyBoundary"/>'s method surface).
/// </summary>
public sealed class CloudInventoryItemPropertiesGateway
{
    private readonly CloudDbContext _context;

    public CloudInventoryItemPropertiesGateway(CloudDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> UpsertAsync(
        uint biotaId,
        string shardId,
        string name,
        ItemType itemType,
        WeenieType weenieType,
        int? value,
        int? burden,
        string? iconCacheKeyHex,
        long revision,
        CancellationToken cancellationToken = default)
    {
        var current = await _context.CloudInventoryItemPropertiesProjections
            .SingleOrDefaultAsync(row => row.BiotaId == biotaId, cancellationToken);

        var (row, applied) = CloudInventoryItemPropertiesProjection.TryApply(
            current, biotaId, shardId, name, itemType, weenieType, value, burden, iconCacheKeyHex, revision);

        if (!applied)
        {
            return false;
        }

        if (current is null)
        {
            _context.CloudInventoryItemPropertiesProjections.Add(row);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
