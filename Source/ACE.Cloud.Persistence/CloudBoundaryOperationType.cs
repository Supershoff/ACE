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

    /// <summary>
    /// Opens an exclusive local Withdrawal Reservation for a Withdrawal Token (WDR-001), one event
    /// per locked target -- whole Cloud Item or Cloud Stack Lot quantity, in any mix (issue #122).
    /// </summary>
    WithdrawalReservationOpened,

    /// <summary>
    /// Cancels a local Withdrawal Reservation before redemption (WDR-003), one event per target it
    /// had locked.
    /// </summary>
    WithdrawalReservationCancelled,

    /// <summary>
    /// Marks a local Withdrawal Reservation's redemption idempotency record (issue #122); the actual
    /// per-target custody-to-world transitions it performs are recorded as ordinary
    /// <see cref="Withdrawal"/> or <see cref="StackWithdrawal"/> ledger/outbox events, one per target,
    /// sharing this redemption's correlation ID.
    /// </summary>
    WithdrawalReservationRedeemed,

    /// <summary>
    /// Converts a raw Pyreal coin-stack deposit into MMDs plus an updated Pyreal Remainder
    /// (DEP-006). Each created MMD also gets its own ordinary <see cref="Deposit"/>-typed ledger and
    /// outbox event, sharing this event's correlation ID, so the companion web sees new MMDs exactly
    /// like any other deposited Cloud Item; this event anchors the conversion itself (consumed raw
    /// biota, amounts) at the ledger level.
    /// </summary>
    PyrealConversion,

    /// <summary>Withdraws all or part of an account's Pyreal Remainder as raw Pyreal coin stacks (DEP-006).</summary>
    PyrealRemainderWithdrawal,

    /// <summary>
    /// Moves one whole-item Cloud Custody Record or Cloud Stack Lot from a former monarch's
    /// Allegiance Vault into their new monarch's, as part of a Vault Absorption (VAULT-004).
    /// </summary>
    VaultAbsorption,

    /// <summary>
    /// Reassigns a whole (non-stack) Cloud Custody Record to a new owner outside any typed
    /// reservation's fulfillment -- the core custody state model's "immediate cloud transfer" edge
    /// (<see cref="CloudOwnershipTransferAuthority"/>).
    /// </summary>
    OwnershipTransfer,
}
