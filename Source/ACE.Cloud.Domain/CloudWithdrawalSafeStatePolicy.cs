namespace ACE.Cloud.Domain;

/// <summary>
/// Pure evaluation of WDR-004's safe-state gate: "Redemption requires an alive, fully loaded,
/// non-combat player who is not trading, portaling, recalling, or performing another inventory
/// transfer. Revalidate the token and every item under transaction lock." Every check here is a pure
/// function over <see cref="CloudWithdrawalSafeStateSnapshot"/> so ACE.Server can run the exact same
/// rule both when a player first submits a redemption command and again, cheaply, immediately before
/// the world-boundary transaction commits.
/// </summary>
public static class CloudWithdrawalSafeStatePolicy
{
    public static CloudWithdrawalSafeStateResult Evaluate(CloudWithdrawalSafeStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsAlive)
        {
            return CloudWithdrawalSafeStateResult.Unsafe(
                CloudWithdrawalSafeStateRejectionCode.NotAlive, "You must be alive to redeem a Withdrawal Token.");
        }

        if (!snapshot.IsFullyLoaded)
        {
            return CloudWithdrawalSafeStateResult.Unsafe(
                CloudWithdrawalSafeStateRejectionCode.NotFullyLoaded, "You must be fully loaded into the world to redeem a Withdrawal Token.");
        }

        if (snapshot.IsInCombatMode)
        {
            return CloudWithdrawalSafeStateResult.Unsafe(
                CloudWithdrawalSafeStateRejectionCode.InCombatMode, "You cannot redeem a Withdrawal Token while in combat mode.");
        }

        if (snapshot.IsTrading)
        {
            return CloudWithdrawalSafeStateResult.Unsafe(
                CloudWithdrawalSafeStateRejectionCode.Trading, "You cannot redeem a Withdrawal Token while trading.");
        }

        if (snapshot.IsTeleporting)
        {
            return CloudWithdrawalSafeStateResult.Unsafe(
                CloudWithdrawalSafeStateRejectionCode.Teleporting, "You cannot redeem a Withdrawal Token while portaling or recalling.");
        }

        if (snapshot.IsLoggingOut)
        {
            return CloudWithdrawalSafeStateResult.Unsafe(
                CloudWithdrawalSafeStateRejectionCode.LoggingOut, "You cannot redeem a Withdrawal Token while logging out.");
        }

        if (snapshot.IsPerformingAnotherTransfer)
        {
            return CloudWithdrawalSafeStateResult.Unsafe(
                CloudWithdrawalSafeStateRejectionCode.PerformingAnotherTransfer,
                "You cannot redeem a Withdrawal Token while another inventory transfer is in progress.");
        }

        return CloudWithdrawalSafeStateResult.Safe();
    }
}
