namespace ACE.Cloud.Domain;

/// <summary>
/// The exact unit an exclusive Cloud reservation locks: one whole non-stack Cloud Item, or one
/// specific Cloud Stack Lot's quantity claim (IMPLEMENTATION-BRIEF.md's core custody state model:
/// "One quantity may have at most one exclusive reservation at a time"). A stack's partial quantity
/// is never targeted directly; it must first exist as its own <see cref="CloudStackLotId"/>
/// (ARCH-010, INV-002).
/// </summary>
public sealed record CloudReservationTarget
{
    public CloudReservationTargetKind Kind { get; }

    public CloudItemId? ItemId { get; }

    public CloudStackLotId? StackLotId { get; }

    private CloudReservationTarget(CloudReservationTargetKind kind, CloudItemId? itemId, CloudStackLotId? stackLotId)
    {
        Kind = kind;
        ItemId = itemId;
        StackLotId = stackLotId;
    }

    public static CloudReservationTarget ForItem(CloudItemId itemId)
    {
        ArgumentNullException.ThrowIfNull(itemId);
        return new CloudReservationTarget(CloudReservationTargetKind.Item, itemId, null);
    }

    public static CloudReservationTarget ForStackLot(CloudStackLotId stackLotId)
    {
        ArgumentNullException.ThrowIfNull(stackLotId);
        return new CloudReservationTarget(CloudReservationTargetKind.StackLot, null, stackLotId);
    }

    public override string ToString() => Kind switch
    {
        CloudReservationTargetKind.Item => $"Item {ItemId}",
        CloudReservationTargetKind.StackLot => $"Stack Lot {StackLotId}",
        _ => throw new InvalidOperationException("Unrecognized Cloud reservation target kind."),
    };
}
