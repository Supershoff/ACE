namespace ACE.Cloud.Domain;

/// <summary>
/// The pure decision rule behind world-boundary raw Pyreal Remainder withdrawal (DEP-006's "allow
/// safe raw remainder withdrawal"). This is the same minimal, direct-delivery shape
/// <c>CloudCustodyBoundary.WithdrawAsync</c> already established for an ordinary Cloud Item: no
/// Withdrawal Token/reservation TTL is introduced here, matching that existing method's documented
/// scope boundary ("a later withdrawal-feature issue adds [Withdrawal Token] validation"). Instead:
/// <list type="bullet">
/// <item>"Capacity failure" is an insufficient remainder: the request is refused, the remainder is
/// left completely unchanged, and the caller may retry once the remainder grows (a future deposit)
/// or after lowering the requested amount.</item>
/// <item>"Retry" is the same idempotency-key replay every other <c>CloudCustodyBoundary</c> method
/// already guarantees (ARCH-006, transaction rule 4).</item>
/// <item>"Maintenance" is <see cref="CloudMutationGateState"/> (ADM-004), revalidated by the caller
/// at the exact instant it also locks the remainder row, exactly like
/// <see cref="CloudReservationPolicy"/> and <see cref="CloudOwnershipTransferPolicy"/> already do
/// for their own mutations (transaction rule 9).</item>
/// </list>
/// </summary>
public static class PyrealRemainderWithdrawalPolicy
{
    public static PyrealRemainderWithdrawalDecision Decide(long currentRemainder, long requestedAmount, CloudMutationGateState gateState)
    {
        if (currentRemainder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentRemainder), currentRemainder, "A Pyreal Remainder cannot be negative.");
        }

        if (gateState == CloudMutationGateState.Frozen)
        {
            return PyrealRemainderWithdrawalDecision.Frozen();
        }

        if (requestedAmount <= 0 || requestedAmount > currentRemainder)
        {
            return PyrealRemainderWithdrawalDecision.InsufficientRemainder(currentRemainder);
        }

        return PyrealRemainderWithdrawalDecision.Approved(currentRemainder - requestedAmount);
    }
}
