namespace ACE.Cloud.Domain;

/// <summary>
/// The outcome of revalidating one Cloud Custodian sell window at commit time (DEP-008: "A disabled
/// Custodian must reject a stale open-window commit rather than accept against old configuration";
/// transaction rule 10: "Do not let a stale open Custodian window... bypass current state").
/// </summary>
public sealed record CloudCustodianSaleWindowValidation
{
    public bool IsCurrent { get; }

    public string? StaleReason { get; }

    private CloudCustodianSaleWindowValidation(bool isCurrent, string? staleReason)
    {
        IsCurrent = isCurrent;
        StaleReason = staleReason;
    }

    public static CloudCustodianSaleWindowValidation Current() => new(true, null);

    public static CloudCustodianSaleWindowValidation Stale(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A stale sale window result requires a player-facing reason.", nameof(reason));
        }

        return new CloudCustodianSaleWindowValidation(false, reason);
    }
}

/// <summary>
/// The single pure rule a Cloud Custodian revalidates at sale commit (DEP-008, ADM-003), regardless
/// of how long its sell window has been open: it must still be an enabled Custodian Location, and
/// the administrator configuration must not have changed since the window was captured. Kept
/// independent of ACE.Server so this exact rule can run in a unit test without a live landblock.
/// </summary>
public static class CloudCustodianSaleWindowPolicy
{
    public static CloudCustodianSaleWindowValidation Validate(
        bool isLocationCurrentlyEnabled, CloudAggregateVersion windowOpenedAtConfigVersion, CloudAggregateVersion currentConfigVersion)
    {
        ArgumentNullException.ThrowIfNull(windowOpenedAtConfigVersion);
        ArgumentNullException.ThrowIfNull(currentConfigVersion);

        if (!isLocationCurrentlyEnabled)
        {
            return CloudCustodianSaleWindowValidation.Stale(
                "This Cloud Custodian's location has been disabled or relocated. Please close this window and find it again.");
        }

        if (windowOpenedAtConfigVersion != currentConfigVersion)
        {
            return CloudCustodianSaleWindowValidation.Stale(
                "The Cloud Custodian configuration has changed since you opened this window. Please close it and try again.");
        }

        return CloudCustodianSaleWindowValidation.Current();
    }
}
