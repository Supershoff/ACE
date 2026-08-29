using System.Reflection;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Red/Green coverage for UI-005/UI-006: layer composition, the "any unresolved layer forces the
/// whole result to fallback" rule, and determinism ("Cache hits are bitwise stable for identical
/// complete composition keys").
/// </summary>
[TestClass]
public sealed class CloudIconCompositorTests
{
    private const uint BaseIconDid = 0x06000010;
    private const int ManifestVersion = 7;

    [TestMethod]
    public async Task ComposeAsync_AllLayersResolve_ReturnsComposedWithNoDiagnostics()
    {
        var inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid };
        var layerSource = new FakeCloudIconLayerSource()
            .WithResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, FakeCloudIconLayerSource.SolidRaster(2, 2, 10, 20, 30, 255));

        var result = await CloudIconCompositor.ComposeAsync(
            inputs, ManifestVersion, new FakeCloudIconClothingEffectResolver(), layerSource);

        Assert.AreEqual(CloudIconCompositionOutcomeKind.Composed, result.Outcome);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.IsNotNull(result.ComposedRaster);
        Assert.AreEqual(10, result.ComposedRaster!.Rgba[0]);
        Assert.AreEqual(20, result.ComposedRaster.Rgba[1]);
        Assert.AreEqual(30, result.ComposedRaster.Rgba[2]);
        Assert.AreEqual(255, result.ComposedRaster.Rgba[3]);
    }

    [TestMethod]
    public async Task ComposeAsync_NoBaseIconAvailable_ReturnsFallbackWithMissingDiagnostic()
    {
        var inputs = new CloudIconCompositionInputs();

        var result = await CloudIconCompositor.ComposeAsync(
            inputs, ManifestVersion, new FakeCloudIconClothingEffectResolver(), new FakeCloudIconLayerSource());

        Assert.AreEqual(CloudIconCompositionOutcomeKind.Fallback, result.Outcome);
        Assert.IsNull(result.ComposedRaster);
        Assert.AreEqual(1, result.Diagnostics.Count);
        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Missing, result.Diagnostics[0].Reason);
    }

    [TestMethod]
    [DataRow(CloudIconLayerResolutionOutcomeKind.Missing)]
    [DataRow(CloudIconLayerResolutionOutcomeKind.Corrupt)]
    [DataRow(CloudIconLayerResolutionOutcomeKind.Unsupported)]
    [DataRow(CloudIconLayerResolutionOutcomeKind.Oversized)]
    [DataRow(CloudIconLayerResolutionOutcomeKind.Malicious)]
    public async Task ComposeAsync_OverlayFailsForAnyReason_ReturnsFallbackInsteadOfAPartialIcon(CloudIconLayerResolutionOutcomeKind reason)
    {
        const uint overlayDid = 0x06000030;
        var inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid, OverlayDid = overlayDid };
        var layerSource = new FakeCloudIconLayerSource()
            .WithResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, FakeCloudIconLayerSource.SolidRaster(2, 2, 1, 2, 3, 255))
            .WithFailure(CloudIconLayerKind.Overlay, overlayDid, reason);

        var result = await CloudIconCompositor.ComposeAsync(
            inputs, ManifestVersion, new FakeCloudIconClothingEffectResolver(), layerSource);

        Assert.AreEqual(CloudIconCompositionOutcomeKind.Fallback, result.Outcome);
        Assert.IsNull(result.ComposedRaster);
        Assert.AreEqual(1, result.Diagnostics.Count);
        Assert.AreEqual(reason, result.Diagnostics[0].Reason);
        Assert.AreEqual(CloudIconLayerKind.Overlay, result.Diagnostics[0].Layer.Kind);
    }

    [TestMethod]
    public async Task ComposeAsync_ResolvedLayersWithMismatchedDimensions_ReturnsFallbackWithCorruptDiagnostic()
    {
        var inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid, OverlayDid = 0x06000030 };
        var layerSource = new FakeCloudIconLayerSource()
            .WithResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, FakeCloudIconLayerSource.SolidRaster(2, 2, 1, 2, 3, 255))
            .WithResolved(CloudIconLayerKind.Overlay, 0x06000030, FakeCloudIconLayerSource.SolidRaster(4, 4, 1, 2, 3, 255));

        var result = await CloudIconCompositor.ComposeAsync(
            inputs, ManifestVersion, new FakeCloudIconClothingEffectResolver(), layerSource);

        Assert.AreEqual(CloudIconCompositionOutcomeKind.Fallback, result.Outcome);
        Assert.AreEqual(CloudIconLayerResolutionOutcomeKind.Corrupt, result.Diagnostics[0].Reason);
    }

    [TestMethod]
    public async Task ComposeAsync_MultipleUnresolvedLayers_ReturnsOneDiagnosticPerFailure()
    {
        var inputs = new CloudIconCompositionInputs
        {
            BaseIconDid = BaseIconDid,
            OverlayDid = 0x06000030,
            OverlaySecondaryDid = 0x06000040,
        };
        var layerSource = new FakeCloudIconLayerSource()
            .WithResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, FakeCloudIconLayerSource.SolidRaster(2, 2, 1, 2, 3, 255))
            .WithFailure(CloudIconLayerKind.Overlay, 0x06000030, CloudIconLayerResolutionOutcomeKind.Missing)
            .WithFailure(CloudIconLayerKind.OverlaySecondary, 0x06000040, CloudIconLayerResolutionOutcomeKind.Corrupt);

        var result = await CloudIconCompositor.ComposeAsync(
            inputs, ManifestVersion, new FakeCloudIconClothingEffectResolver(), layerSource);

        Assert.AreEqual(2, result.Diagnostics.Count);
    }

    [TestMethod]
    public async Task ComposeAsync_TransparentOverlayOverOpaqueBase_BlendsAlphaDeterministically()
    {
        const uint overlayDid = 0x06000030;
        var inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid, OverlayDid = overlayDid };
        var layerSource = new FakeCloudIconLayerSource()
            .WithResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, FakeCloudIconLayerSource.SolidRaster(2, 2, 0, 0, 0, 255))
            .WithResolved(CloudIconLayerKind.Overlay, overlayDid, FakeCloudIconLayerSource.SolidRaster(2, 2, 255, 255, 255, 128));

        var result = await CloudIconCompositor.ComposeAsync(
            inputs, ManifestVersion, new FakeCloudIconClothingEffectResolver(), layerSource);

        Assert.AreEqual(CloudIconCompositionOutcomeKind.Composed, result.Outcome);
        // 50%-alpha white over black: every channel should land roughly halfway, and fully opaque.
        Assert.IsTrue(result.ComposedRaster!.Rgba[0] is > 100 and < 155);
        Assert.AreEqual(255, result.ComposedRaster.Rgba[3]);
    }

    [TestMethod]
    public async Task ComposeAsync_SameInputsComposedTwice_ProducesBitwiseIdenticalBytesAndCacheKey()
    {
        const uint overlayDid = 0x06000030;
        var inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid, OverlayDid = overlayDid };

        Task<CloudIconCompositionResult> ComposeOnce()
        {
            var layerSource = new FakeCloudIconLayerSource()
                .WithResolved(CloudIconLayerKind.BaseIcon, BaseIconDid, FakeCloudIconLayerSource.SolidRaster(3, 3, 12, 34, 56, 200))
                .WithResolved(CloudIconLayerKind.Overlay, overlayDid, FakeCloudIconLayerSource.SolidRaster(3, 3, 200, 100, 50, 90));
            return CloudIconCompositor.ComposeAsync(inputs, ManifestVersion, new FakeCloudIconClothingEffectResolver(), layerSource);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ComposeOnce()));

        foreach (var result in results)
        {
            Assert.AreEqual(results[0].CacheKey, result.CacheKey);
            CollectionAssert.AreEqual(results[0].ComposedRaster!.Rgba, result.ComposedRaster!.Rgba);
        }
    }

    /// <summary>UI-006: "Icons contain no animation; magical glow is a still blue layer."</summary>
    [TestMethod]
    public void IconDomainTypes_ExposeNoAnimationOrTimeBasedConcept()
    {
        var forbidden = new[] { "frame", "animat", "duration", "fps", "tick" };
        var iconTypes = new[]
        {
            typeof(CloudIconCompositionInputs), typeof(CloudIconRasterLayer),
            typeof(CloudIconLayerPlan), typeof(CloudIconCompositionResult),
        };

        var offenders = iconTypes
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => forbidden.Any(bad => p.Name.Contains(bad, StringComparison.OrdinalIgnoreCase)))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        Assert.AreEqual(0, offenders.Count, $"Unexpected animation/time-based member: {string.Join(", ", offenders)}");
    }
}
