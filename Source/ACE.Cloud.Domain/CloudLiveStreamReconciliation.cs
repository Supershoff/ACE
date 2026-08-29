namespace ACE.Cloud.Domain;

/// <summary>
/// CONTEXT.md's "every streamed entity carries an authoritative version; optimistic UI is limited to
/// suitable actions and must reconcile or visibly reverse when the committed result differs" (EVT-007).
/// This is the pure comparison a client-facing layer applies once it has both the version it
/// optimistically assumed and the version the Live State Stream actually committed; it holds no
/// client/session state of its own.
/// </summary>
public static class CloudLiveStreamReconciliation
{
    /// <summary>
    /// True when a client that optimistically rendered <paramref name="optimisticSequenceNumber"/>
    /// must visibly reverse that guess because the server's authoritative
    /// <paramref name="authoritativeSequenceNumber"/> turned out to be different. Equal values mean
    /// the optimistic guess was confirmed and needs no reversal; any other value -- including a
    /// smaller authoritative number, which can happen when the optimistic action was rejected outright
    /// and never advanced anything -- must be visibly corrected rather than left showing stale state.
    /// </summary>
    public static bool ShouldReverseOptimisticUpdate(long optimisticSequenceNumber, long authoritativeSequenceNumber) =>
        optimisticSequenceNumber != authoritativeSequenceNumber;
}
