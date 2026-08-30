using ACE.Cloud.Domain;
using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Reads the current <see cref="CloudMutationGateState"/> from Global Cloud Maintenance (ADM-004) for
/// one existing boundary transaction to revalidate against at commit time (transaction rule 9).
/// Deliberately a plain read within the calling transaction, not a locked one: Global Cloud
/// Maintenance changes rarely, and every ordinary mutation only needs to observe it, never to block
/// it from changing. This is what lets every existing Cloud Transaction Authority call site that used
/// to hardcode <see cref="CloudMutationGateState.Open"/> (see that enum's own doc comment) resolve the
/// real gate with a single extra query against the caller's own <see cref="CloudDbContext"/>, without
/// changing any constructor signature.
///
/// Every current caller (custody, reservation, ownership transfer, account link/unlink, Allegiance
/// Vault Absorption) is a non-marketplace mutation, so this resolves <see cref="CloudMutationGatePolicy.ResolveGlobal"/>
/// alone and never widens to Marketplace Maintenance Frozen (MKT-204), which is scoped to marketplace
/// mutations and must not block these call sites (see <see cref="CloudMutationGatePolicy"/>'s own doc
/// comment). A future marketplace-scoped mutation needs its own reader built on
/// <see cref="CloudMutationGatePolicy.ResolveMarketplace"/> instead of this one.
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

        return CloudMutationGatePolicy.ResolveGlobal(globalMaintenanceIsFrozen);
    }
}
