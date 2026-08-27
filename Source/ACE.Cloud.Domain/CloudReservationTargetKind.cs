namespace ACE.Cloud.Domain;

/// <summary>
/// The kind of unit a <see cref="CloudReservationTarget"/> identifies.
/// </summary>
public enum CloudReservationTargetKind
{
    /// <summary>A whole non-stack Cloud Item, identified by its native biota GUID.</summary>
    Item,

    /// <summary>One specific Cloud Stack Lot's quantity claim.</summary>
    StackLot,
}
