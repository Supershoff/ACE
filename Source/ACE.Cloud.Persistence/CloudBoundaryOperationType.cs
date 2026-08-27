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
}
