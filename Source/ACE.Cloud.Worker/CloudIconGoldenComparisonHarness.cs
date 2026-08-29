using System.Security.Cryptography;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Worker;

/// <summary>
/// The protected multi-fixture golden harness for Icon Reconstruction (ASSET-005, UI-005, UI-006),
/// extending the single validated reference item <c>CloudIconCompositionGoldenTests</c> proved end to
/// end for issue #24/#26. Only ever compares content hashes: <see cref="CloudIconGoldenFixture.ExpectedPngSha256Hex"/>
/// in, a <see cref="CloudFidelityPhaseGateFixtureResult"/> out -- the operator's real DAT bytes and
/// composed PNGs never leave this call, matching issue #28's "retain only redacted machine-readable
/// pass/fail metadata; never upload source art or private captures to GitHub."
/// </summary>
public static class CloudIconGoldenComparisonHarness
{
    public static async Task<IReadOnlyList<CloudFidelityPhaseGateFixtureResult>> CompareAsync(
        IReadOnlyList<CloudIconGoldenFixture> fixtures,
        int manifestVersion,
        ICloudIconClothingEffectResolver clothingEffectResolver,
        ICloudIconLayerSource layerSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        ArgumentNullException.ThrowIfNull(clothingEffectResolver);
        ArgumentNullException.ThrowIfNull(layerSource);

        var results = new List<CloudFidelityPhaseGateFixtureResult>(fixtures.Count);

        foreach (var fixture in fixtures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await CompareOneAsync(fixture, manifestVersion, clothingEffectResolver, layerSource, cancellationToken));
        }

        return results;
    }

    private static async Task<CloudFidelityPhaseGateFixtureResult> CompareOneAsync(
        CloudIconGoldenFixture fixture,
        int manifestVersion,
        ICloudIconClothingEffectResolver clothingEffectResolver,
        ICloudIconLayerSource layerSource,
        CancellationToken cancellationToken)
    {
        var composition = await CloudIconCompositor.ComposeAsync(fixture.Inputs, manifestVersion, clothingEffectResolver, layerSource, cancellationToken);

        if (composition.Outcome != CloudIconCompositionOutcomeKind.Composed)
        {
            var fallbackReasons = composition.Diagnostics
                .Select(d => $"{d.Layer.Kind}:{d.Layer.Did:x8} did not resolve ({d.Reason})")
                .ToList();

            return new CloudFidelityPhaseGateFixtureResult
            {
                Category = "Icon",
                FixtureName = fixture.FixtureName,
                Matched = false,
                Differences = fallbackReasons.Count > 0 ? fallbackReasons : ["Composition unexpectedly fell back with no diagnostics."],
            };
        }

        var pngBytes = CloudIconPngEncoder.Encode(composition.ComposedRaster!);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(pngBytes));
        var matched = string.Equals(actualHash, fixture.ExpectedPngSha256Hex, StringComparison.OrdinalIgnoreCase);

        return new CloudFidelityPhaseGateFixtureResult
        {
            Category = "Icon",
            FixtureName = fixture.FixtureName,
            Matched = matched,
            Differences = matched ? Array.Empty<string>() : [$"expected PNG sha256 {fixture.ExpectedPngSha256Hex}, got {actualHash}"],
        };
    }
}
