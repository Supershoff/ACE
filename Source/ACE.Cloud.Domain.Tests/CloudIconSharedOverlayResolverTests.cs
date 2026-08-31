using ACE.Cloud.Domain;
using ACE.Entity.Enum;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red/Green coverage for issue #34's follow-up human-acceptance correction: the runtime worker
/// already composes <see cref="CloudIconCompositionInputs.ItemTypeBackgroundDid"/> and
/// <see cref="CloudIconCompositionInputs.UiEffectDids"/> when present, but nothing ever resolved
/// either from the operator's configured mapping. Proves <see cref="CloudIconSharedOverlayResolver"/>
/// resolves both deterministically, without ever fabricating a DID for an unconfigured category/effect.
/// </summary>
[TestClass]
public sealed class CloudIconSharedOverlayResolverTests
{
    [TestMethod]
    public void ResolveItemTypeBackgroundDid_ConfiguredCategory_ReturnsItsDid()
    {
        var backgrounds = new Dictionary<string, uint> { [nameof(CloudInventoryCategory.MeleeWeapons)] = 0x060011D3 };

        var did = CloudIconSharedOverlayResolver.ResolveItemTypeBackgroundDid(
            ItemType.MeleeWeapon, WeenieType.Undef, backgrounds);

        Assert.AreEqual(0x060011D3u, did);
    }

    [TestMethod]
    public void ResolveItemTypeBackgroundDid_UnconfiguredCategory_ReturnsNullRatherThanFabricating()
    {
        var backgrounds = new Dictionary<string, uint> { [nameof(CloudInventoryCategory.Armor)] = 0x06000001 };

        var did = CloudIconSharedOverlayResolver.ResolveItemTypeBackgroundDid(
            ItemType.MeleeWeapon, WeenieType.Undef, backgrounds);

        Assert.IsNull(did);
    }

    [TestMethod]
    public void ResolveItemTypeBackgroundDid_UsesTheSameDeterministicPriorityAsTheMulePageGrid()
    {
        // An item carrying both MeleeWeapon and the broader MagicWieldable flag must resolve the same
        // category CloudInventoryCategoryClassifier would (MeleeWeapons), not a background for whatever
        // flag happens to be checked first.
        var backgrounds = new Dictionary<string, uint>
        {
            [nameof(CloudInventoryCategory.MeleeWeapons)] = 0x06000001,
            [nameof(CloudInventoryCategory.Miscellaneous)] = 0x06000002,
        };

        var did = CloudIconSharedOverlayResolver.ResolveItemTypeBackgroundDid(
            ItemType.MeleeWeapon | ItemType.MagicWieldable, WeenieType.Undef, backgrounds);

        Assert.AreEqual(0x06000001u, did);
    }

    [TestMethod]
    public void ResolveUiEffectDids_NoActiveEffects_ReturnsEmpty()
    {
        var overlays = new Dictionary<string, uint> { [nameof(UiEffects.Magical)] = 0x06000777 };

        var dids = CloudIconSharedOverlayResolver.ResolveUiEffectDids(null, overlays);

        Assert.HasCount(0, dids);
    }

    [TestMethod]
    public void ResolveUiEffectDids_MagicalFlagConfigured_ReturnsItsDid()
    {
        var overlays = new Dictionary<string, uint> { [nameof(UiEffects.Magical)] = 0x06000777 };

        var dids = CloudIconSharedOverlayResolver.ResolveUiEffectDids(UiEffects.Magical, overlays);

        Assert.HasCount(1, dids);
        Assert.AreEqual(0x06000777u, dids[0]);
    }

    [TestMethod]
    public void ResolveUiEffectDids_UnconfiguredFlag_ReturnsEmptyRatherThanFabricating()
    {
        var overlays = new Dictionary<string, uint>();

        var dids = CloudIconSharedOverlayResolver.ResolveUiEffectDids(UiEffects.Magical, overlays);

        Assert.HasCount(0, dids);
    }

    [TestMethod]
    public void ResolveUiEffectDids_MultipleActiveFlags_ReturnsThemInDeclaredFlagOrder()
    {
        var overlays = new Dictionary<string, uint>
        {
            [nameof(UiEffects.Magical)] = 0x06000001,
            [nameof(UiEffects.Fire)] = 0x06000002,
        };

        var dids = CloudIconSharedOverlayResolver.ResolveUiEffectDids(UiEffects.Fire | UiEffects.Magical, overlays);

        CollectionAssert.AreEqual(new[] { 0x06000001u, 0x06000002u }, dids.ToList());
    }
}
