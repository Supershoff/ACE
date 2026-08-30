namespace ACE.Cloud.Domain;

/// <summary>
/// One Cloud Item or Cloud Stack Lot as seen by <see cref="CloudInventoryQueryEngine"/>, before
/// authorization scoping, category filtering, sorting, and paging are applied. <see cref="ItemId"/>
/// plus <see cref="StackLotId"/> together are its stable identity (issue #30 Green: "stable
/// item/custody/lot identity"): <see cref="StackLotId"/> is null for a whole (non-stack) Cloud Item
/// and set for one lot of a stackable biota (ARCH-010, INV-001), so two lots of the same biota are
/// always distinct rows even though they share <see cref="ItemId"/>.
/// </summary>
public sealed record CloudInventoryQueryCandidate(
    CloudItemId ItemId,
    CloudStackLotId? StackLotId,
    Guid OwnerId,
    string Name,
    CloudInventoryCategory Category,
    int Quantity,
    int? Value,
    int? Burden,
    bool IsReserved,
    CloudAggregateVersion Version,
    string? IconCacheKeyHex = null) : ICloudInventorySortable;
