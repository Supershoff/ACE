namespace ACE.Cloud.Domain;

/// <summary>
/// The idempotent, order-tolerant apply rule every Custody/Identity Outbox projection consumer uses
/// (ARCH-007, transaction rule 6: outbox effects are delivered at least once, so consumers must be
/// idempotent). An outbox event's own <c>SequenceNumber</c> is already a durable, strictly
/// increasing total order (assigned transactionally by <c>CloudCustodyOutboxSequence</c> /
/// <c>CloudIdentityOutboxSequence</c>), so comparing it against the highest sequence number a
/// projection row has already applied is sufficient to make consumption safe under duplicate
/// delivery, delayed delivery, and newer-before-older (out-of-order) delivery alike, without needing
/// a separate per-aggregate version counter.
/// </summary>
public static class CloudProjectionSequenceGuard
{
    /// <summary>
    /// True when an incoming event should be applied to a projection row: either the row has never
    /// applied anything yet (<paramref name="lastAppliedSequenceNumber"/> is null), or the incoming
    /// event is strictly newer than what the row has already applied. A duplicate
    /// (<c>incoming == lastApplied</c>) or stale/regressive (<c>incoming &lt; lastApplied</c>)
    /// delivery must never be applied, which is what keeps a clean rebuild (replaying every event in
    /// order from empty state) and ordinary incremental consumption (which may see duplicates or
    /// reordering from retries) converge to the exact same projected state.
    /// </summary>
    public static bool ShouldApply(long? lastAppliedSequenceNumber, long incomingSequenceNumber)
    {
        if (incomingSequenceNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(incomingSequenceNumber), "An outbox event requires a positive sequence number.");
        }

        return lastAppliedSequenceNumber is null || incomingSequenceNumber > lastAppliedSequenceNumber.Value;
    }
}
