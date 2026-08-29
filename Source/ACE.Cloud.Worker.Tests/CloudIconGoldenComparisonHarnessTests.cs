using System.Security.Cryptography;
using ACE.Cloud.Domain;
using ACE.Cloud.Worker;

namespace ACE.Cloud.Worker.Tests;

/// <summary>
/// Synthetic (no operator DAT required) coverage for <see cref="CloudIconGoldenComparisonHarness"/>
/// itself -- proving the harness's own match/mismatch/fallback reporting is correct is always-on CI
/// evidence, independent from <c>CloudIconCompositionGoldenTests.Compare_OperatorOwnedIconFixtureCorpus_EveryFixtureMatches</c>'s
/// protected run against a real curated corpus (issue #28's Refactor requirement: "keep synthetic CI
/// coverage distinct from protected golden verification").
/// </summary>
[TestClass]
public sealed class CloudIconGoldenComparisonHarnessTests
{
    private const uint BaseIconDid = 0x06000010;

    [TestMethod]
    public async Task CompareAsync_MatchingExpectedHash_ReportsAMatch()
    {
        var layerSource = new FakeLayerSource().WithSolid(CloudIconLayerKind.BaseIcon, BaseIconDid, 10, 20, 30, 255);
        var expectedHash = await ComposeAndHashAsync(layerSource);
        var fixture = new CloudIconGoldenFixture
        {
            FixtureName = "solid-base-icon",
            Inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid },
            ExpectedPngSha256Hex = expectedHash,
        };

        var results = await CloudIconGoldenComparisonHarness.CompareAsync([fixture], manifestVersion: 1, new FakeClothingResolver(), layerSource);

        Assert.HasCount(1, results);
        Assert.IsTrue(results[0].Matched);
        Assert.AreEqual("Icon", results[0].Category);
        Assert.HasCount(0, results[0].Differences);
    }

    [TestMethod]
    public async Task CompareAsync_WrongExpectedHash_ReportsAMismatchWithNoRawBytes()
    {
        var layerSource = new FakeLayerSource().WithSolid(CloudIconLayerKind.BaseIcon, BaseIconDid, 10, 20, 30, 255);
        var fixture = new CloudIconGoldenFixture
        {
            FixtureName = "solid-base-icon",
            Inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid },
            ExpectedPngSha256Hex = new string('0', 64),
        };

        var results = await CloudIconGoldenComparisonHarness.CompareAsync([fixture], manifestVersion: 1, new FakeClothingResolver(), layerSource);

        Assert.IsFalse(results[0].Matched);
        Assert.HasCount(1, results[0].Differences);
        StringAssert.Contains(results[0].Differences[0], "expected PNG sha256");
    }

    [TestMethod]
    public async Task CompareAsync_UnresolvableFixture_ReportsAMismatchNamingTheFailedLayer()
    {
        var fixture = new CloudIconGoldenFixture
        {
            FixtureName = "missing-base-icon",
            Inputs = new CloudIconCompositionInputs(), // no base icon at all: unresolvable
            ExpectedPngSha256Hex = new string('0', 64),
        };

        var results = await CloudIconGoldenComparisonHarness.CompareAsync([fixture], manifestVersion: 1, new FakeClothingResolver(), new FakeLayerSource());

        Assert.IsFalse(results[0].Matched);
        StringAssert.Contains(results[0].Differences[0], "did not resolve");
    }

    [TestMethod]
    public async Task CompareAsync_MultipleFixtures_ReportsOneResultPerFixtureInOrder()
    {
        var layerSource = new FakeLayerSource().WithSolid(CloudIconLayerKind.BaseIcon, BaseIconDid, 1, 2, 3, 255);
        var expectedHash = await ComposeAndHashAsync(layerSource);
        var fixtures = new[]
        {
            new CloudIconGoldenFixture { FixtureName = "a", Inputs = new CloudIconCompositionInputs { BaseIconDid = BaseIconDid }, ExpectedPngSha256Hex = expectedHash },
            new CloudIconGoldenFixture { FixtureName = "b", Inputs = new CloudIconCompositionInputs(), ExpectedPngSha256Hex = new string('0', 64) },
        };

        var results = await CloudIconGoldenComparisonHarness.CompareAsync(fixtures, manifestVersion: 1, new FakeClothingResolver(), layerSource);

        Assert.HasCount(2, results);
        Assert.AreEqual("a", results[0].FixtureName);
        Assert.IsTrue(results[0].Matched);
        Assert.AreEqual("b", results[1].FixtureName);
        Assert.IsFalse(results[1].Matched);
    }

    private static async Task<string> ComposeAndHashAsync(FakeLayerSource layerSource)
    {
        var composition = await CloudIconCompositor.ComposeAsync(
            new CloudIconCompositionInputs { BaseIconDid = BaseIconDid }, manifestVersion: 1, new FakeClothingResolver(), layerSource);
        var pngBytes = CloudIconPngEncoder.Encode(composition.ComposedRaster!);
        return Convert.ToHexStringLower(SHA256.HashData(pngBytes));
    }

    private sealed class FakeClothingResolver : ICloudIconClothingEffectResolver
    {
        public Task<CloudIconClothingResolution?> ResolveAsync(
            uint clothingBaseDid, uint setupTableId, int? paletteTemplate, float? shade, CancellationToken cancellationToken = default)
            => Task.FromResult<CloudIconClothingResolution?>(null);
    }

    private sealed class FakeLayerSource : ICloudIconLayerSource
    {
        private readonly Dictionary<CloudIconLayerReference, CloudIconRasterLayer> _rasters = new();

        public FakeLayerSource WithSolid(CloudIconLayerKind kind, uint did, byte r, byte g, byte b, byte a)
        {
            var rgba = new byte[] { r, g, b, a, r, g, b, a, r, g, b, a, r, g, b, a };
            _rasters[new CloudIconLayerReference(kind, did)] = new CloudIconRasterLayer(2, 2, rgba);
            return this;
        }

        public Task<CloudIconLayerResolution> ResolveAsync(
            CloudIconLayerReference reference, IReadOnlyList<CloudIconPaletteRangeOverride> paletteOverrides, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rasters.TryGetValue(reference, out var raster)
                ? CloudIconLayerResolution.Resolved(raster)
                : CloudIconLayerResolution.Failed(CloudIconLayerResolutionOutcomeKind.Missing));
    }
}
