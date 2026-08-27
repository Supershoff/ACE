namespace ACE.Cloud.Persistence;

/// <summary>
/// The kind of world-boundary handoff a <see cref="CloudIdempotencyRecord"/>,
/// <see cref="CloudActivityLedgerEvent"/>, or <see cref="CloudCustodyOutboxEvent"/> represents
/// (ARCH-002, ARCH-006).
/// </summary>
public enum CloudBoundaryOperationType
{
    Deposit,
    Withdrawal,

    /// <summary>
    /// Deposits a stackable biota into Cloud custody, creating a stack CloudCustodyRecord and its
    /// initial single CloudStackLot (ARCH-010).
    /// </summary>
    StackDeposit,

    /// <summary>
    /// Withdraws quantity from a Cloud Stack Lot, delivering either the original backing biota
    /// (the lot was the last one holding the whole stack) or a materialized child biota (ARCH-010,
    /// INV-003).
    /// </summary>
    StackWithdrawal,

    /// <summary>Opens an exclusive local Withdrawal Reservation for a Withdrawal Token (WDR-001).</summary>
    WithdrawalReservationOpened,

    /// <summary>Cancels a local Withdrawal Reservation before redemption (WDR-003).</summary>
    WithdrawalReservationCancelled,

    /// <summary>
    /// Redeems a local Withdrawal Reservation, atomically performing the same custody-to-world
    /// transition as <see cref="Withdrawal"/> and releasing the reservation as fulfilled.
    /// </summary>
    WithdrawalReservationRedeemed,
}
