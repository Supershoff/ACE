namespace ACE.Cloud.Domain;

/// <summary>
/// The exact live facts <see cref="CloudWithdrawalSafeStatePolicy"/> needs to decide whether a
/// character is currently safe to redeem a Withdrawal Token (WDR-004: "a living, fully loaded,
/// non-combat character who is not trading, portaling, recalling, logging out, or performing another
/// transfer"). ACE.Server builds this from the live <c>Player</c> immediately before redemption, and
/// again inside the world-thread transaction lock so the check is revalidated at the exact instant of
/// redemption, not only when the player first submitted the token.
/// </summary>
public sealed record CloudWithdrawalSafeStateSnapshot(
    bool IsAlive,
    bool IsFullyLoaded,
    bool IsInCombatMode,
    bool IsTrading,
    bool IsTeleporting,
    bool IsLoggingOut,
    bool IsPerformingAnotherTransfer);
