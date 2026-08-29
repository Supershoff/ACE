using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red/Green coverage for UI-005's layer resolution rules: base Icon DID, ClothingBase/
/// PaletteTemplate/Shade resolution (including tailoring, which is exactly a different ClothingBase/
/// PaletteTemplate/SetupTableId on the same item), underlay, overlay/secondary behavior, and layer
/// order, all without any DAT bytes or a live manifest.
/// </summary>
[TestClass]
public sealed class CloudIconLayerPlannerTests
{
    [TestMethod]
    public async Task PlanAsync_BaseIconOnly_ProducesOneUnorderedLayer()
    {
        var inputs = new CloudIconCompositionInputs { BaseIconDid = 0x06000001 };

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, new FakeCloudIconClothingEffectResolver());

        Assert.AreEqual(1, plan.Layers.Count);
        Assert.AreEqual(new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000001u), plan.BaseIcon);
    }

    [TestMethod]
    public async Task PlanAsync_EveryOptionalLayerPresent_OrdersBackgroundUnderlayBaseOverlayOverlaySecondaryThenUiEffects()
    {
        var inputs = new CloudIconCompositionInputs
        {
            BaseIconDid = 0x06000010,
            ItemTypeBackgroundDid = 0x06000001,
            UnderlayDid = 0x06000002,
            OverlayDid = 0x06000003,
            OverlaySecondaryDid = 0x06000004,
            UiEffectDids = [0x06000005, 0x06000006],
        };

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, new FakeCloudIconClothingEffectResolver());

        var expectedOrder = new[]
        {
            CloudIconLayerKind.Background,
            CloudIconLayerKind.Underlay,
            CloudIconLayerKind.BaseIcon,
            CloudIconLayerKind.Overlay,
            CloudIconLayerKind.OverlaySecondary,
            CloudIconLayerKind.UiEffect,
            CloudIconLayerKind.UiEffect,
        };

        CollectionAssert.AreEqual(expectedOrder, plan.Layers.Select(l => l.Kind).ToList());
        Assert.AreEqual(0x06000005u, plan.Layers[5].Did);
        Assert.AreEqual(0x06000006u, plan.Layers[6].Did);
    }

    [TestMethod]
    public async Task PlanAsync_OnlyBackgroundAndOverlaySecondaryDeclared_OmitsUnderlayAndOverlay()
    {
        var inputs = new CloudIconCompositionInputs
        {
            BaseIconDid = 0x06000010,
            ItemTypeBackgroundDid = 0x06000001,
            OverlaySecondaryDid = 0x06000004,
        };

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, new FakeCloudIconClothingEffectResolver());

        CollectionAssert.AreEqual(
            new[] { CloudIconLayerKind.Background, CloudIconLayerKind.BaseIcon, CloudIconLayerKind.OverlaySecondary },
            plan.Layers.Select(l => l.Kind).ToList());
    }

    [TestMethod]
    public async Task PlanAsync_ClothingBaseResolvesIconOverride_ReplacesBaseIconDid()
    {
        var inputs = new CloudIconCompositionInputs
        {
            BaseIconDid = 0x06000010,
            ClothingBaseDid = 0x10000001,
            SetupTableId = 0x02000001,
            PaletteTemplate = 3,
        };

        var paletteOverrides = new[] { new CloudIconPaletteRangeOverride(0x04000001, 0, 8) };
        var resolver = new FakeCloudIconClothingEffectResolver()
            .With(0x10000001, 0x02000001, new CloudIconClothingResolution(0x06000099, paletteOverrides));

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, resolver);

        Assert.AreEqual(new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000099u), plan.BaseIcon);
        CollectionAssert.AreEqual(paletteOverrides, plan.BaseIconPaletteOverrides.ToList());
    }

    [TestMethod]
    public async Task PlanAsync_ClothingBaseResolvesPaletteOnlyEffect_KeepsBaseIconDidButAppliesPaletteOverrides()
    {
        var inputs = new CloudIconCompositionInputs
        {
            BaseIconDid = 0x06000010,
            ClothingBaseDid = 0x10000001,
            SetupTableId = 0x02000001,
            Shade = 0.5f,
        };

        var paletteOverrides = new[] { new CloudIconPaletteRangeOverride(0x04000002, 4, 4) };
        var resolver = new FakeCloudIconClothingEffectResolver()
            .With(0x10000001, 0x02000001, new CloudIconClothingResolution(null, paletteOverrides));

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, resolver);

        Assert.AreEqual(new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000010u), plan.BaseIcon);
        CollectionAssert.AreEqual(paletteOverrides, plan.BaseIconPaletteOverrides.ToList());
    }

    [TestMethod]
    public async Task PlanAsync_IgnoreCloIconsSet_KeepsBaseIconDidDespiteClothingOverride()
    {
        var inputs = new CloudIconCompositionInputs
        {
            BaseIconDid = 0x06000010,
            ClothingBaseDid = 0x10000001,
            SetupTableId = 0x02000001,
            IgnoreCloIcons = true,
        };

        var resolver = new FakeCloudIconClothingEffectResolver()
            .With(0x10000001, 0x02000001, new CloudIconClothingResolution(0x06000099, []));

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, resolver);

        Assert.AreEqual(new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000010u), plan.BaseIcon);
    }

    [TestMethod]
    public async Task PlanAsync_ClothingBaseHasNoEffectForSetup_FallsBackToBaseIconDid()
    {
        var inputs = new CloudIconCompositionInputs
        {
            BaseIconDid = 0x06000010,
            ClothingBaseDid = 0x10000001,
            SetupTableId = 0x02000099, // no effect registered for this setup
        };

        var resolver = new FakeCloudIconClothingEffectResolver()
            .With(0x10000001, 0x02000001, new CloudIconClothingResolution(0x06000099, []));

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, resolver);

        Assert.AreEqual(new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000010u), plan.BaseIcon);
    }

    [TestMethod]
    public async Task PlanAsync_TailoredItem_DifferentClothingBaseAndPaletteTemplateProducesDifferentPlan()
    {
        var vanilla = new CloudIconCompositionInputs
        {
            ClothingBaseDid = 0x10000001,
            SetupTableId = 0x02000001,
            PaletteTemplate = 0,
        };

        var tailored = vanilla with { ClothingBaseDid = 0x10000002, PaletteTemplate = 5 };

        var resolver = new FakeCloudIconClothingEffectResolver()
            .With(0x10000001, 0x02000001, new CloudIconClothingResolution(0x06000001, []))
            .With(0x10000002, 0x02000001, new CloudIconClothingResolution(0x06000002, []));

        var vanillaPlan = await CloudIconLayerPlanner.PlanAsync(vanilla, resolver);
        var tailoredPlan = await CloudIconLayerPlanner.PlanAsync(tailored, resolver);

        Assert.AreNotEqual(vanillaPlan.BaseIcon, tailoredPlan.BaseIcon);
    }

    [TestMethod]
    public async Task PlanAsync_NoBaseIconAndNoClothingResolution_ProducesUnresolvedBaseIconSentinel()
    {
        var inputs = new CloudIconCompositionInputs();

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, new FakeCloudIconClothingEffectResolver());

        Assert.IsTrue(plan.BaseIcon.IsUnresolvable);
        Assert.AreEqual(0u, plan.BaseIcon.Did);
    }

    [TestMethod]
    public async Task PlanAsync_ImbuedItem_UiEffectGlowLayerAppearsLastAfterOverlays()
    {
        var inputs = new CloudIconCompositionInputs
        {
            BaseIconDid = 0x06000010,
            OverlayDid = 0x06000003,
            UiEffectDids = [0x06000777], // e.g. imbue/magical glow still layer
        };

        var plan = await CloudIconLayerPlanner.PlanAsync(inputs, new FakeCloudIconClothingEffectResolver());

        Assert.AreEqual(CloudIconLayerKind.UiEffect, plan.Layers[^1].Kind);
        Assert.AreEqual(0x06000777u, plan.Layers[^1].Did);
    }
}
