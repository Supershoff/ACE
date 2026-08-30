namespace ACE.Cloud.Domain;

/// <summary>
/// Pure validated transitions over a <see cref="CloudGlobalMaintenanceState"/> (ADM-004): "Entry/exit
/// require reason, confirmation, ledger event, and admin webhook. Never cancel or unlock
/// automatically." This class only decides whether a requested entry/exit is valid; the caller is
/// responsible for persisting the ledger event, invoking the admin webhook, and -- on exit --
/// shifting every open expiry clock by <see cref="CloudGlobalMaintenanceChangeResult.FrozenDuration"/>.
/// Every method here is a pure function over its inputs, matching every other Cloud policy in this
/// namespace.
/// </summary>
public static class CloudGlobalMaintenancePolicy
{
    public static CloudGlobalMaintenanceChangeResult Enter(
        CloudGlobalMaintenanceState current,
        string reason,
        bool confirmed,
        uint actorAccessLevel,
        uint actorAccountId,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!CloudAdminAccessRevalidationPolicy.Evaluate(actorAccessLevel).IsAuthorized)
        {
            return CloudGlobalMaintenanceChangeResult.Failure(
                "Only an administrator (ace_auth.account.accessLevel == 5) may enter Global Cloud Maintenance.");
        }

        if (current.IsFrozen)
        {
            return CloudGlobalMaintenanceChangeResult.Failure("Global Cloud Maintenance is already active.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return CloudGlobalMaintenanceChangeResult.Failure("Entering Global Cloud Maintenance requires a reason.");
        }

        if (!confirmed)
        {
            return CloudGlobalMaintenanceChangeResult.Failure("Entering Global Cloud Maintenance requires explicit confirmation.");
        }

        var next = current with
        {
            IsFrozen = true,
            Reason = reason,
            EnteredAtUtc = nowUtc,
            EnteredByAccountId = actorAccountId,
            Version = current.Version.Next(),
        };

        return CloudGlobalMaintenanceChangeResult.Success(next);
    }

    /// <summary>
    /// Ends an active freeze, exactly restoring open mutations. The returned
    /// <see cref="CloudGlobalMaintenanceChangeResult.FrozenDuration"/> is the exact wall-clock span
    /// between <see cref="CloudGlobalMaintenanceState.EnteredAtUtc"/> and <paramref name="nowUtc"/>
    /// (both database time, transaction rule 1) that every open expiry clock must be shifted by --
    /// never approximated, never rounded.
    /// </summary>
    public static CloudGlobalMaintenanceChangeResult Exit(
        CloudGlobalMaintenanceState current,
        bool confirmed,
        uint actorAccessLevel,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!CloudAdminAccessRevalidationPolicy.Evaluate(actorAccessLevel).IsAuthorized)
        {
            return CloudGlobalMaintenanceChangeResult.Failure(
                "Only an administrator (ace_auth.account.accessLevel == 5) may exit Global Cloud Maintenance.");
        }

        if (!current.IsFrozen || current.EnteredAtUtc is null)
        {
            return CloudGlobalMaintenanceChangeResult.Failure("Global Cloud Maintenance is not currently active.");
        }

        if (!confirmed)
        {
            return CloudGlobalMaintenanceChangeResult.Failure("Exiting Global Cloud Maintenance requires explicit confirmation.");
        }

        if (nowUtc < current.EnteredAtUtc.Value)
        {
            throw new ArgumentOutOfRangeException(nameof(nowUtc), "Exit time cannot precede the recorded entry time.");
        }

        var frozenDuration = nowUtc - current.EnteredAtUtc.Value;

        var next = current with
        {
            IsFrozen = false,
            Reason = null,
            EnteredAtUtc = null,
            EnteredByAccountId = null,
            Version = current.Version.Next(),
        };

        return CloudGlobalMaintenanceChangeResult.Success(next, frozenDuration);
    }
}
