using ACE.Entity.Enum;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Read access to one biota's <see cref="CloudInventoryItemPropertiesProjection"/> row (issue #31:
/// the minimum player-facing fields -- name, value, burden -- a Full Cloud Appraisal panel can be
/// built from until a future ACE-side raw-property capture integration lands a fuller snapshot,
/// exactly the same deferral <see cref="CloudInventoryItemPropertiesProjection"/>'s own doc comment
/// already accepts for category classification). Interface-extracted so
/// <c>ACE.Cloud.Backend.Tests</c> can substitute an in-memory fake for endpoint tests.
/// </summary>
public interface ICloudInventoryItemPropertiesGateway
{
    Task<CloudInventoryItemPropertiesProjection?> TryGetAsync(uint biotaId, string shardId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Idempotent upsert access to <see cref="CloudInventoryItemPropertiesProjection"/> (issue #30
/// Green). Kept as its own narrow gateway, separate from <see cref="CloudCustodyBoundary"/> (ACE's
/// World Boundary Authority gateway) and from <see cref="CloudCustodyProjectionConsumer"/> (the
/// Custody Outbox consumer): writing an item's display properties is neither a custody state
/// transition nor an outbox-ordered event, so it does not belong on either of those surfaces (see
/// <c>CloudWorldBoundaryAuthoritySurfaceTests</c>, which would fail this build if a Cloud-only
/// concept leaked onto <see cref="CloudCustodyBoundary"/>'s method surface).
/// </summary>
public sealed class CloudInventoryItemPropertiesGateway : ICloudInventoryItemPropertiesGateway
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

    public async Task<CloudInventoryItemPropertiesProjection?> TryGetAsync(
        uint biotaId, string shardId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Reading a properties row requires a Cloud Shard ID.", nameof(shardId));
        }

        return await _context.CloudInventoryItemPropertiesProjections
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.BiotaId == biotaId && row.ShardId == shardId, cancellationToken);
    }
}
