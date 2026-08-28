namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Coverage for <see cref="CloudCustodianLocationResolver.Resolve"/> (DEP-007: default Marketplace
/// and mansion sets, independent toggles; "each enabled Custodian occupies one location").
/// </summary>
[TestClass]
public sealed class CloudCustodianLocationResolverTests
{
    private static readonly CloudCustodianPosition MarketplacePosition =
        CloudCustodianPosition.TryParse("0x016C01BC [49.206000 -31.935000 0.005000] 0.707107 0.000000 0.000000 -0.707107")!;

    private static readonly CloudCustodianPosition MansionPosition =
        CloudCustodianPosition.TryParse("0x0000A9FE [50.000000 60.000000 0.000000] 1.000000 0.000000 0.000000 0.000000")!;

    private static readonly CloudCustodianMansionLocation Mansion = new(0xA9FE0001, MansionPosition);

    [TestMethod]
    public void Resolve_DefaultConfiguration_IncludesMarketplaceAndEveryMansion()
    {
        var configuration = CloudCustodianConfiguration.Default();

        var resolved = CloudCustodianLocationResolver.Resolve(configuration, MarketplacePosition, [Mansion]);

        Assert.HasCount(2, resolved);
        CollectionAssert.Contains(resolved.Select(l => l.Key).ToList(), CloudCustodianLocationKey.Marketplace);
        CollectionAssert.Contains(resolved.Select(l => l.Key).ToList(), CloudCustodianLocationKey.ForMansion(Mansion.MansionGuid));
    }

    [TestMethod]
    public void Resolve_MarketplaceDisabled_OmitsOnlyMarketplace()
    {
        var configuration = CloudCustodianConfigurationPolicy.SetMarketplaceEnabled(CloudCustodianConfiguration.Default(), false).Configuration!;

        var resolved = CloudCustodianLocationResolver.Resolve(configuration, MarketplacePosition, [Mansion]);

        Assert.HasCount(1, resolved);
        Assert.AreEqual(CloudCustodianLocationKey.ForMansion(Mansion.MansionGuid), resolved[0].Key);
    }

    [TestMethod]
    public void Resolve_MansionsDisabled_OmitsEveryMansionButKeepsMarketplace()
    {
        var configuration = CloudCustodianConfigurationPolicy.SetMansionsEnabled(CloudCustodianConfiguration.Default(), false).Configuration!;

        var resolved = CloudCustodianLocationResolver.Resolve(configuration, MarketplacePosition, [Mansion]);

        Assert.HasCount(1, resolved);
        Assert.AreEqual(CloudCustodianLocationKey.Marketplace, resolved[0].Key);
    }

    [TestMethod]
    public void Resolve_BothSharedSetsDisabled_ResolvesOnlyCustomPositions()
    {
        var configuration = CloudCustodianConfiguration.Default();
        configuration = CloudCustodianConfigurationPolicy.SetMarketplaceEnabled(configuration, false).Configuration!;
        configuration = CloudCustodianConfigurationPolicy.SetMansionsEnabled(configuration, false).Configuration!;
        configuration = CloudCustodianConfigurationPolicy.AddCustomPosition(
            configuration, Guid.NewGuid(), "0x00030146 [1.000000 2.000000 3.000000] 1.000000 0.000000 0.000000 0.000000").Configuration!;

        var resolved = CloudCustodianLocationResolver.Resolve(configuration, MarketplacePosition, [Mansion]);

        Assert.HasCount(1, resolved);
        Assert.AreEqual(CloudCustodianLocationKind.Custom, resolved[0].Key.Kind);
    }

    [TestMethod]
    public void Resolve_ACustomPositionDuplicatingMarketplace_ResolvesToOnlyOneLocation()
    {
        var configuration = CloudCustodianConfigurationPolicy.AddCustomPosition(
            CloudCustodianConfiguration.Default(), Guid.NewGuid(), MarketplacePosition.Raw).Configuration!;

        var resolved = CloudCustodianLocationResolver.Resolve(configuration, MarketplacePosition, [Mansion]);

        // Marketplace (source order) wins the slot; the duplicate custom position resolves to nothing extra
        // (DEP-007: "each enabled Custodian occupies one location").
        Assert.HasCount(2, resolved);
        Assert.AreEqual(1, resolved.Count(l => l.Position.Equals(MarketplacePosition)));
    }

    [TestMethod]
    public void Resolve_NoMansionsSupplied_StillResolvesMarketplace()
    {
        var configuration = CloudCustodianConfiguration.Default();

        var resolved = CloudCustodianLocationResolver.Resolve(configuration, MarketplacePosition, []);

        Assert.HasCount(1, resolved);
        Assert.AreEqual(CloudCustodianLocationKey.Marketplace, resolved[0].Key);
    }
}
