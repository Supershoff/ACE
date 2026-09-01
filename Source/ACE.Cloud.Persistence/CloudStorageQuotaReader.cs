using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// Reads the current shard-wide Storage Quota limits (INV-004) for a boundary transaction to
/// revalidate a new count-increasing obligation against at commit time (transaction rule 9). A plain
/// read within the calling transaction, matching <see cref="CloudMutationGateReader"/>: limits change
/// rarely and every ordinary deposit only needs to observe the current value.
/// </summary>
public static class CloudStorageQuotaReader
{
    public static async Task<int?> GetPersonalLimitAsync(CloudDbContext context, string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Reading Storage Quota limits requires a Cloud Shard ID.", nameof(shardId));
        }

        var row = await context.Set<CloudStorageQuotaLimitsRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.ShardId == shardId, cancellationToken);

        return row?.PersonalLimit;
    }

    /// <summary>The independently limited Allegiance Vault Storage Quota scope (INV-004's <see cref="CloudStorageQuotaScope.AllegianceVault"/>).</summary>
    public static async Task<int?> GetVaultLimitAsync(CloudDbContext context, string shardId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("Reading Storage Quota limits requires a Cloud Shard ID.", nameof(shardId));
        }

        var row = await context.Set<CloudStorageQuotaLimitsRecord>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.ShardId == shardId, cancellationToken);

        return row?.VaultLimit;
    }
}
