namespace ACE.Cloud.Domain;

/// <summary>
/// Pure Storage Quota rules (INV-004, INV-005, INV-006). Limit changes are validated and versioned
/// here, matching every other Cloud singleton admin-config policy; <see cref="CheckNewObligation"/> is
/// the one check every count-increasing action (a deposit today; a future accepted offer, purchase,
/// or vault take once those workflows exist) must pass before it may proceed. Settlement of an
/// already-binding obligation (a confirmed Buy It Now, an accepted offer, Vault Absorption) is
/// deliberately never checked here (INV-006): once accepted, it must complete even if it pushes the
/// recipient over a since-lowered limit, leaving them reduce-only afterward (INV-005) rather than
/// failing outright.
/// </summary>
public static class CloudStorageQuotaPolicy
{
    public static CloudStorageQuotaLimitsChangeResult SetPersonalLimit(
        CloudStorageQuotaLimits current, int? limit, uint actorAccessLevel)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!CloudAdminAccessRevalidationPolicy.Evaluate(actorAccessLevel).IsAuthorized)
        {
            return CloudStorageQuotaLimitsChangeResult.Failure(
                "Only an administrator (ace_auth.account.accessLevel == 5) may change Storage Quota limits.");
        }

        if (limit is <= 0)
        {
            return CloudStorageQuotaLimitsChangeResult.Failure("A Storage Quota limit must be a positive item count, or null for unlimited.");
        }

        if (current.PersonalLimit == limit)
        {
            return CloudStorageQuotaLimitsChangeResult.Success(current);
        }

        return CloudStorageQuotaLimitsChangeResult.Success(current with { PersonalLimit = limit, Version = current.Version.Next() });
    }

    public static CloudStorageQuotaLimitsChangeResult SetVaultLimit(
        CloudStorageQuotaLimits current, int? limit, uint actorAccessLevel)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!CloudAdminAccessRevalidationPolicy.Evaluate(actorAccessLevel).IsAuthorized)
        {
            return CloudStorageQuotaLimitsChangeResult.Failure(
                "Only an administrator (ace_auth.account.accessLevel == 5) may change Storage Quota limits.");
        }

        if (limit is <= 0)
        {
            return CloudStorageQuotaLimitsChangeResult.Failure("A Storage Quota limit must be a positive item count, or null for unlimited.");
        }

        if (current.VaultLimit == limit)
        {
            return CloudStorageQuotaLimitsChangeResult.Success(current);
        }

        return CloudStorageQuotaLimitsChangeResult.Success(current with { VaultLimit = limit, Version = current.Version.Next() });
    }

    /// <summary>
    /// Refuses a new count-increasing obligation once <paramref name="currentProjectedCount"/> --
    /// native biotas plus projected materialized Cloud Stack Lots, excluding the one Pyreal Remainder
    /// (INV-004) -- would reach or exceed <paramref name="limit"/> once <paramref name="additionalCount"/>
    /// more items are added (an ordinary deposit adds exactly one; a Raw Pyreal conversion that mints
    /// several MMDs in one commit adds all of them at once, since nothing else can observe -- and
    /// therefore separately gate -- the count in between). A null limit is always unlimited. An owner
    /// sitting at or above a since-lowered limit is refused every further count-increasing action
    /// (reduce-only, INV-005) until it withdraws below the limit or the limit is raised/removed.
    /// </summary>
    public static CloudStorageQuotaCheckResult CheckNewObligation(int? limit, int currentProjectedCount, int additionalCount = 1)
    {
        if (currentProjectedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentProjectedCount), "A projected item count cannot be negative.");
        }

        if (additionalCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(additionalCount), "A new obligation must add at least one item.");
        }

        if (limit is null)
        {
            return CloudStorageQuotaCheckResult.Success();
        }

        return currentProjectedCount + additionalCount <= limit.Value
            ? CloudStorageQuotaCheckResult.Success()
            : CloudStorageQuotaCheckResult.Failure(
                $"This owner's Storage Quota of {limit.Value} does not have room for {additionalCount} more item(s) "
                    + $"({currentProjectedCount} already counted); it is reduce-only until items are withdrawn or the quota is raised.");
    }
}
