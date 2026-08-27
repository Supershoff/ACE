using Microsoft.EntityFrameworkCore;

namespace ACE.Cloud.Persistence;

/// <summary>
/// INV-004's Storage Quota measure: "native biotas plus projected biotas for independently
/// materializable Cloud Stack Lots." A stack Cloud Custody Record that has never been split still
/// has exactly one CloudStackLot (created together with the record by
/// <see cref="CloudCustodyBoundary.DepositStackAsync"/>), so "one stackable biota counts as one
/// item" and "each additional independently materializable Cloud Stack Lot counts as one projected
/// item" (CONTEXT.md) both reduce to the same rule: count every lot an owner holds. Which lot, if
/// any, eventually keeps the original GUID at withdrawal cannot be known in advance (it depends on
/// withdrawal order), so every lot must be counted as its own potential materialization.
///
/// Counting is a pure read against CloudCustodyRecord/CloudStackLot; it never touches ace_shard and
/// never allocates or references a native GUID (issue #5's Red requirement: "no GUID is allocated
/// early").
/// </summary>
public static class CloudStackQuotaProjection
{
    public static async Task<int> CountProjectedItemsAsync(
        CloudDbContext context, string shardId, Guid ownerId, CancellationToken cancellationToken = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrWhiteSpace(shardId))
        {
            throw new ArgumentException("A quota projection requires a Cloud Shard ID.", nameof(shardId));
        }

        if (ownerId == Guid.Empty)
        {
            throw new ArgumentException("A quota projection requires an owner.", nameof(ownerId));
        }

        // Non-stack Cloud Items: each is one native biota under one Cloud Custody Record.
        var nonStackCount = await context.CloudCustodyRecords
            .CountAsync(r => r.ShardId == shardId && r.OwnerId == ownerId, cancellationToken);

        // Every Cloud Stack Lot this owner holds, regardless of which stack backs it.
        var stackLotCount = await context.CloudStackLots
            .CountAsync(l => l.ShardId == shardId && l.OwnerId == ownerId, cancellationToken);

        return nonStackCount + stackLotCount;
    }
}
