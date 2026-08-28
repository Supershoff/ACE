namespace ACE.Cloud.Domain;

/// <summary>
/// The exact reason <see cref="CloudWithdrawalSafeStatePolicy"/> refused a redemption (WDR-004,
/// WDR-005: "return exact actionable in-game failures").
/// </summary>
public enum CloudWithdrawalSafeStateRejectionCode
{
    NotAlive,
    NotFullyLoaded,
    InCombatMode,
    Trading,
    Teleporting,
    LoggingOut,
    PerformingAnotherTransfer,
}
