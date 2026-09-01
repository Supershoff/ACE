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

    /// <summary>
    /// A Transfer Offer was opened (XFER-001, XFER-002), one ledger event per offered target sharing
    /// one correlation ID. Ledger-only: unlike <see cref="OwnershipTransfer"/> this never reaches the
    /// Custody Outbox, since no custody or ownership changes until the offer is later accepted.
    /// </summary>
    TransferOfferCreated,

    /// <summary>The sender cancelled a pending Transfer Offer before acceptance (XFER-002). Ledger-only, like <see cref="TransferOfferCreated"/>.</summary>
    TransferOfferCancelled,

    /// <summary>The recipient declined a pending Transfer Offer (XFER-002). Ledger-only, like <see cref="TransferOfferCreated"/>.</summary>
    TransferOfferDeclined,

    /// <summary>A pending Transfer Offer's seven-day deadline passed unresolved (XFER-002). Ledger-only, like <see cref="TransferOfferCreated"/>.</summary>
    TransferOfferExpired,

    /// <summary>
    /// A grant-derived Withdrawal Reservation was released before redemption because the Sharing
    /// Grant that authorized it was downgraded/revoked (issue #36, SHARE-004). Ledger-only, like
    /// <see cref="WithdrawalReservationCancelled"/>, which this otherwise mirrors.
    /// </summary>
    WithdrawalReservationInvalidated,

    /// <summary>
    /// An Acting Character contributed a whole Cloud Item or Cloud Stack Lot from their own personal
    /// Cloud Inventory into their currently authorized Allegiance Vault (issue #37, VAULT-001,
    /// VAULT-003). Distinct from <see cref="OwnershipTransfer"/> so the Activity Ledger records the
    /// direction of every vault movement precisely, matching <see cref="VaultAbsorption"/>'s own
    /// established precedent of a vault-specific operation type.
    /// </summary>
    VaultContribution,

    /// <summary>
    /// An Acting Character took a whole Cloud Item or Cloud Stack Lot from their currently authorized
    /// Allegiance Vault into their own personal Cloud Inventory (issue #37, VAULT-001, VAULT-003).
    /// </summary>
    VaultTake,

    /// <summary>
    /// An administrator moved one whole-item Cloud Custody Record or Cloud Stack Lot out of an
    /// orphaned Allegiance Vault (one whose monarch was deleted out-of-band) into an explicitly
    /// chosen destination, as an audited VAULT-005 recovery (issue #38, ADM-002). Distinct from
    /// <see cref="VaultAbsorption"/> because this is never automatic and never guesses a successor.
    /// </summary>
    AdminVaultRecovery,
}
