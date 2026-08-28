namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Table-driven coverage for WDR-006's "allowed by default in Marketplace and any landblock
/// containing player housing/SlumLord... custom locations are admin-named landblocks... withdraw
/// anywhere is an audited shard-wide bypass and defaults off."
/// </summary>
[TestClass]
public sealed class CloudWithdrawalLocationPolicyTests
{
    private static CloudWithdrawalLocationSnapshot NoneEligible() => new(
        IsMarketplace: false, IsHousingLandblock: false, IsNamedWithdrawalLandblock: false, WithdrawAnywhereEnabled: false);

    [TestMethod]
    public void IsEligible_NoQualifyingFlags_IsFalse()
    {
        Assert.IsFalse(CloudWithdrawalLocationPolicy.IsEligible(NoneEligible()));
    }

    [TestMethod]
    public void IsEligible_Marketplace_IsTrue()
    {
        Assert.IsTrue(CloudWithdrawalLocationPolicy.IsEligible(NoneEligible() with { IsMarketplace = true }));
    }

    [TestMethod]
    public void IsEligible_HousingLandblock_IsTrue()
    {
        Assert.IsTrue(CloudWithdrawalLocationPolicy.IsEligible(NoneEligible() with { IsHousingLandblock = true }));
    }

    [TestMethod]
    public void IsEligible_NamedWithdrawalLandblock_IsTrue()
    {
        Assert.IsTrue(CloudWithdrawalLocationPolicy.IsEligible(NoneEligible() with { IsNamedWithdrawalLandblock = true }));
    }

    [TestMethod]
    public void IsEligible_WithdrawAnywhereEnabled_IsTrueEvenWithNoOtherQualifyingLocation()
    {
        Assert.IsTrue(CloudWithdrawalLocationPolicy.IsEligible(NoneEligible() with { WithdrawAnywhereEnabled = true }));
    }
}
