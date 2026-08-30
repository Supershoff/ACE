namespace ACE.Cloud.Domain;

/// <summary>
/// The minimal fields <see cref="CloudInventoryItemOrderPolicy"/> needs to order one Mule Page row,
/// independent of where the row actually came from (a real projection query or a Domain.Tests
/// fixture). <see cref="ItemId"/> doubles as the mandatory final tie-break (UI-003).
/// </summary>
public sealed record CloudInventorySortableItem(CloudItemId ItemId, string Name, int? Value, int? Burden) : ICloudInventorySortable;
