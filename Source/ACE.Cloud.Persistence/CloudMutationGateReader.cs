using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Reads the current <see cref="CloudMutationGateState"/> from Global Cloud Maintenance and
/// Marketplace State (ADM-004, MKT-204) for one existing boundary transaction to revalidate against
/// at commit time (transaction rule 9). Deliberately a plain read within the calling transaction, not
/// a locked one: Global Cloud Maintenance and Marketplace State change rarely, and every ordinary
/// mutation only needs to observe them, never to block them from changing. This is what lets every
/// existing Cloud Transaction Authority call site that used to hardcode
/// <see cref="CloudMutationGateState.Open"/> (see that enum's own doc comment) resolve the real gate
/// with a single extra query against the caller's own <see cref="CloudDbContext"/>, without changing
/// any constructor signature.
/// </summary>
public static class CloudMutationGateReader
{
    public static async Task<CloudMutationGateState> ResolveAsync(CloudDbContext context, string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Resolving the Cloud mutation gate requires a Cloud Shard ID.", nameof(shardId));
        }

        var maintenanceRow = await context.Set<CloudGlobalMaintenanceRecord>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShardId == shardId, cancellationToken);
        var globalMaintenanceIsFrozen = maintenanceRow?.IsFrozen ?? false;

        var marketplaceRow = await context.Set<CloudMarketplaceConfigurationRecord>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.ShardId == shardId, cancellationToken);
        var marketplaceState = marketplaceRow?.State ?? CloudMarketplaceState.Enabled;

        return CloudMutationGatePolicy.Resolve(globalMaintenanceIsFrozen, marketplaceState);
    }
}
