namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Coverage for <see cref="CloudCustodianSaleWindowPolicy.Validate"/> (DEP-008 Red section: "Open a
/// vendor window, disable or relocate its Custodian, then prove a stale sell commit is rejected";
/// transaction rule 10).
/// </summary>
[TestClass]
public sealed class CloudCustodianSaleWindowPolicyTests
{
    [TestMethod]
    public void Validate_SameVersionAndStillEnabled_IsCurrent()
    {
        var version = CloudAggregateVersion.Initial;

        var result = CloudCustodianSaleWindowPolicy.Validate(isLocationCurrentlyEnabled: true, version, version);

        Assert.IsTrue(result.IsCurrent);
        Assert.IsNull(result.StaleReason);
    }

    [TestMethod]
    public void Validate_TheConfigurationChangedSinceTheWindowOpened_IsStale()
    {
        var openedAt = CloudAggregateVersion.Initial;
        var current = openedAt.Next();

        var result = CloudCustodianSaleWindowPolicy.Validate(isLocationCurrentlyEnabled: true, openedAt, current);

        Assert.IsFalse(result.IsCurrent);
        Assert.IsNotNull(result.StaleReason);
    }

    [TestMethod]
    public void Validate_TheLocationIsNoLongerEnabled_IsStaleEvenAtTheSameVersion()
    {
        var version = CloudAggregateVersion.Initial;

        var result = CloudCustodianSaleWindowPolicy.Validate(isLocationCurrentlyEnabled: false, version, version);

        Assert.IsFalse(result.IsCurrent);
        Assert.IsNotNull(result.StaleReason);
    }

    [TestMethod]
    public void Validate_DisabledAndVersionChanged_IsStale()
    {
        var openedAt = CloudAggregateVersion.Initial;
        var current = openedAt.Next();

        var result = CloudCustodianSaleWindowPolicy.Validate(isLocationCurrentlyEnabled: false, openedAt, current);

        Assert.IsFalse(result.IsCurrent);
    }
}
