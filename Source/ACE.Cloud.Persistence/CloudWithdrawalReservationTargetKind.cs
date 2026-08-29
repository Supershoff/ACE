namespace ACE.Cloud.Persistence;

/// <summary>
/// The kind of unit a single <see cref="CloudWithdrawalReservationTarget"/> within one Withdrawal
/// Token's reservation locks: a whole non-stack Cloud Item, or a quantity claim against one Cloud
/// Stack Lot (issue #122, WDR-001, INV-002). Mirrors <see cref="ACE.Cloud.Domain.CloudReservationTargetKind"/>
/// one-for-one; this persistence-level copy exists so the persisted column has its own stable string
/// representation independent of the pure domain enum.
/// </summary>
public enum CloudWithdrawalReservationTargetKind
{
    Item,
    StackLot,
}
