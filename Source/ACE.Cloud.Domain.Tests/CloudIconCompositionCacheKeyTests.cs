using System.Reflection;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// UI-006 Red/Green coverage: "Vary every composition input independently and prove the cache key
/// changes; vary stack count/selection/reservation and prove it does not."
/// </summary>
[TestClass]
public sealed class CloudIconCompositionCacheKeyTests
{
    private static readonly CloudIconLayerReference BaseIcon = new(CloudIconLayerKind.BaseIcon, 0x06000010);

    [TestMethod]
    public void Create_SameLayersAndManifestVersion_ProducesTheSameKey()
    {
        var plan = new CloudIconLayerPlan(BaseIcon, [], [BaseIcon]);

        var first = CloudIconCompositionCacheKey.Create(plan, manifestVersion: 3);
        var second = CloudIconCompositionCacheKey.Create(plan, manifestVersion: 3);

        Assert.AreEqual(first, second);
        Assert.AreEqual(first.Hex, second.Hex);
    }

    [TestMethod]
    public void Create_DifferentManifestVersion_ProducesADifferentKey()
    {
        var plan = new CloudIconLayerPlan(BaseIcon, [], [BaseIcon]);

        var v1 = CloudIconCompositionCacheKey.Create(plan, manifestVersion: 1);
        var v2 = CloudIconCompositionCacheKey.Create(plan, manifestVersion: 2);

        Assert.AreNotEqual(v1, v2);
    }

    [TestMethod]
    public void Create_DifferentBackgroundDid_ProducesADifferentKey()
    {
        var background1 = new CloudIconLayerReference(CloudIconLayerKind.Background, 0x06000001);
        var background2 = new CloudIconLayerReference(CloudIconLayerKind.Background, 0x06000002);

        var plan1 = new CloudIconLayerPlan(BaseIcon, [], [background1, BaseIcon]);
        var plan2 = new CloudIconLayerPlan(BaseIcon, [], [background2, BaseIcon]);

        Assert.AreNotEqual(
            CloudIconCompositionCacheKey.Create(plan1, 1),
            CloudIconCompositionCacheKey.Create(plan2, 1));
    }

    [TestMethod]
    public void Create_DifferentUnderlayDid_ProducesADifferentKey()
    {
        var underlay1 = new CloudIconLayerReference(CloudIconLayerKind.Underlay, 0x06000001);
        var underlay2 = new CloudIconLayerReference(CloudIconLayerKind.Underlay, 0x06000002);

        Assert.AreNotEqual(
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [underlay1, BaseIcon]), 1),
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [underlay2, BaseIcon]), 1));
    }

    [TestMethod]
    public void Create_DifferentBaseIconDid_ProducesADifferentKey()
    {
        var base1 = new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000010);
        var base2 = new CloudIconLayerReference(CloudIconLayerKind.BaseIcon, 0x06000011);

        Assert.AreNotEqual(
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(base1, [], [base1]), 1),
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(base2, [], [base2]), 1));
    }

    [TestMethod]
    public void Create_DifferentOverlayDid_ProducesADifferentKey()
    {
        var overlay1 = new CloudIconLayerReference(CloudIconLayerKind.Overlay, 0x06000003);
        var overlay2 = new CloudIconLayerReference(CloudIconLayerKind.Overlay, 0x06000004);

        Assert.AreNotEqual(
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [BaseIcon, overlay1]), 1),
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [BaseIcon, overlay2]), 1));
    }

    [TestMethod]
    public void Create_DifferentOverlaySecondaryDid_ProducesADifferentKey()
    {
        var secondary1 = new CloudIconLayerReference(CloudIconLayerKind.OverlaySecondary, 0x06000005);
        var secondary2 = new CloudIconLayerReference(CloudIconLayerKind.OverlaySecondary, 0x06000006);

        Assert.AreNotEqual(
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [BaseIcon, secondary1]), 1),
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [BaseIcon, secondary2]), 1));
    }

    [TestMethod]
    public void Create_DifferentUiEffects_ProducesADifferentKey()
    {
        var effect1 = new CloudIconLayerReference(CloudIconLayerKind.UiEffect, 0x06000777);
        var effect2 = new CloudIconLayerReference(CloudIconLayerKind.UiEffect, 0x06000778);

        Assert.AreNotEqual(
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [BaseIcon, effect1]), 1),
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [BaseIcon, effect2]), 1));
    }

    [TestMethod]
    public void Create_DifferentUiEffectOrder_ProducesADifferentKey()
    {
        var effect1 = new CloudIconLayerReference(CloudIconLayerKind.UiEffect, 0x06000777);
        var effect2 = new CloudIconLayerReference(CloudIconLayerKind.UiEffect, 0x06000778);

        Assert.AreNotEqual(
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [BaseIcon, effect1, effect2]), 1),
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, [], [BaseIcon, effect2, effect1]), 1));
    }

    [TestMethod]
    public void Create_DifferentPaletteOverride_ProducesADifferentKey()
    {
        var overrides1 = new[] { new CloudIconPaletteRangeOverride(0x04000001, 0, 8) };
        var overrides2 = new[] { new CloudIconPaletteRangeOverride(0x04000002, 0, 8) };

        Assert.AreNotEqual(
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, overrides1, [BaseIcon]), 1),
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, overrides2, [BaseIcon]), 1));
    }

    [TestMethod]
    public void Create_DifferentPaletteOffsetOrLength_ProducesADifferentKey()
    {
        var byOffset = new[] { new CloudIconPaletteRangeOverride(0x04000001, 0, 8) };
        var byLength = new[] { new CloudIconPaletteRangeOverride(0x04000001, 0, 16) };

        Assert.AreNotEqual(
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, byOffset, [BaseIcon]), 1),
            CloudIconCompositionCacheKey.Create(new CloudIconLayerPlan(BaseIcon, byLength, [BaseIcon]), 1));
    }

    /// <summary>
    /// Issue #31 Green: the web client only ever holds the already-composed
    /// <see cref="CloudInventoryQueryResultItem.IconCacheKeyHex"/> string, never a resolved
    /// <see cref="CloudIconLayerPlan"/>, so serving an icon back to it requires reconstructing a
    /// <see cref="CloudIconCompositionCacheKey"/> from that persisted hex alone. This must stay a
    /// narrow, strictly validated parse -- never a free-form path segment -- so a caller can never
    /// turn an arbitrary string into a blob-store path via this type.
    /// </summary>
    [TestMethod]
    public void FromHex_WellFormedLowercaseSha256Hex_RoundTripsTheSameHex()
    {
        var hex = new string('a', 64);

        var key = CloudIconCompositionCacheKey.FromHex(hex);

        Assert.AreEqual(hex, key.Hex);
    }

    [TestMethod]
    [DataRow("", DisplayName = "empty")]
    [DataRow(null, DisplayName = "null")]
    [DataRow("AA", DisplayName = "too short")]
    [DataRow("../../etc/passwd", DisplayName = "path traversal attempt")]
    [DataRow("icon-cache/foo.png", DisplayName = "embedded path separators")]
    public void FromHex_NotSixtyFourLowercaseHexCharacters_Throws(string? hex)
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudIconCompositionCacheKey.FromHex(hex!));
    }

    [TestMethod]
    public void FromHex_UppercaseHex_Throws()
    {
        // Convert.ToHexStringLower is the only producer of a real cache key's Hex, so an uppercase
        // string can never be one of our own keys -- rejecting it outright avoids silently
        // normalizing a caller-supplied value into a path.
        Assert.ThrowsExactly<ArgumentException>(() => CloudIconCompositionCacheKey.FromHex(new string('A', 64)));
    }

    /// <summary>
    /// UI-006: "Stack counts, selection, reservation, and other web state remain separate overlays
    /// and never alter the reconstructed source icon." There is no field on
    /// <see cref="CloudIconCompositionInputs"/> that could hold such state, so it structurally cannot
    /// reach the cache key; this reflection guard keeps that true as the type evolves.
    /// </summary>
    [TestMethod]
    public void CompositionInputs_HasNoStackSelectionOrReservationProperty()
    {
        var forbiddenSubstrings = new[] { "stack", "quantity", "select", "reserv", "count" };

        var offendingProperties = typeof(CloudIconCompositionInputs)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => forbiddenSubstrings.Any(bad => p.Name.Contains(bad, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        Assert.AreEqual(0, offendingProperties.Count, $"Unexpected web-state property on CloudIconCompositionInputs: {string.Join(", ", offendingProperties)}");
    }
}
