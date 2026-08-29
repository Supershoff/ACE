namespace ACE.Cloud.Persistence;

// Retired by issue #122: a Cloud Stack Lot's Withdrawal Reservation is no longer a separate
// per-target-type aggregate/table with its own TokenHash uniqueness constraint (which let the same
// token secret address two independently consumable reservations at once, one whole-item and one
// stack-lot). It is now one CloudWithdrawalReservationTarget row (see that type) within the single
// unified CloudWithdrawalReservation aggregate that every target kind shares.
