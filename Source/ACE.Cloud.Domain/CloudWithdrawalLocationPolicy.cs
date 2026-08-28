namespace ACE.Cloud.Domain;

/// <summary>
/// Pure evaluation of WDR-006: "Allowed by default in Marketplace and any landblock containing
/// player housing/SlumLord. Custom locations are admin-named landblocks. withdraw anywhere is an
/// audited shard-wide bypass and defaults off. Custodian positions are unrelated." Every check here
/// is a pure function over <see cref="CloudWithdrawalLocationSnapshot"/> so the exact same rule runs
/// identically in a unit test and from the world-thread redemption command.
/// </summary>
public static class CloudWithdrawalLocationPolicy
{
    public static bool IsEligible(CloudWithdrawalLocationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.WithdrawAnywhereEnabled
            || snapshot.IsMarketplace
            || snapshot.IsHousingLandblock
            || snapshot.IsNamedWithdrawalLandblock;
    }
}
