namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of one <see cref="CloudGlobalMaintenancePolicy"/> transition: either the new state
/// (and, for <see cref="CloudGlobalMaintenancePolicy.Exit"/>, the exact duration mutations were
/// frozen for -- ADM-004's "resume by shifting deadlines exactly"), or a rejection reason an
/// administrator command can display directly.
/// </summary>
public sealed record CloudGlobalMaintenanceChangeResult
{
    public bool IsSuccess { get; }

    public CloudGlobalMaintenanceState? State { get; }

    /// <summary>
    /// Populated only by a successful <see cref="CloudGlobalMaintenancePolicy.Exit"/>: the exact
    /// wall-clock duration this maintenance freeze was active for, which every open Withdrawal
    /// Reservation's expiry must be shifted by (ADM-004).
    /// </summary>
    public TimeSpan? FrozenDuration { get; }

    public string? Reason { get; }

    private CloudGlobalMaintenanceChangeResult(bool isSuccess, CloudGlobalMaintenanceState? state, TimeSpan? frozenDuration, string? reason)
    {
        IsSuccess = isSuccess;
        State = state;
        FrozenDuration = frozenDuration;
        Reason = reason;
    }

    public static CloudGlobalMaintenanceChangeResult Success(CloudGlobalMaintenanceState state, TimeSpan? frozenDuration = null) =>
        new(true, state ?? throw new ArgumentNullException(nameof(state)), frozenDuration, null);

    public static CloudGlobalMaintenanceChangeResult Failure(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A rejected Global Cloud Maintenance change requires a reason.", nameof(reason));
        }

        return new CloudGlobalMaintenanceChangeResult(false, null, null, reason);
    }
}
