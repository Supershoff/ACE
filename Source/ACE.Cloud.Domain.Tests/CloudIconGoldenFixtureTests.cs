using System.Text.Json;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Proves a <see cref="CloudIconGoldenFixture"/> JSON-round-trips exactly as
/// <see cref="CloudGoldenFixtureLoader.LoadFromDirectory{T}"/> requires -- including
/// <see cref="CloudIconCompositionInputs.UiEffectDids"/>, the one <c>IReadOnlyList&lt;uint&gt;</c>
/// field on the composition inputs a curated fixture file must be able to express (ASSET-005:
/// "magical UI effects").
/// </summary>
[TestClass]
public sealed class CloudIconGoldenFixtureTests
{
    [TestMethod]
    public void CloudIconGoldenFixture_JsonRoundTrips_IncludingUiEffectDids()
    {
        var fixture = new CloudIconGoldenFixture
        {
            FixtureName = "magical-glow-overlay",
            Inputs = new CloudIconCompositionInputs
            {
                BaseIconDid = 0x06000010,
                OverlayDid = 0x06000030,
                UiEffectDids = [0x06000050, 0x06000051],
            },
            ExpectedPngSha256Hex = new string('a', 64),
        };

        var json = JsonSerializer.Serialize(fixture);
        var roundTripped = JsonSerializer.Deserialize<CloudIconGoldenFixture>(json);

        Assert.IsNotNull(roundTripped);
        Assert.AreEqual(fixture.FixtureName, roundTripped!.FixtureName);
        Assert.AreEqual(fixture.Inputs.BaseIconDid, roundTripped.Inputs.BaseIconDid);
        Assert.AreEqual(fixture.Inputs.OverlayDid, roundTripped.Inputs.OverlayDid);
        CollectionAssert.AreEqual(fixture.Inputs.UiEffectDids.ToList(), roundTripped.Inputs.UiEffectDids.ToList());
        Assert.AreEqual(fixture.ExpectedPngSha256Hex, roundTripped.ExpectedPngSha256Hex);
    }

    [TestMethod]
    public void CloudGoldenFixtureLoader_LoadsIconFixturesFromADirectory_OrderedByFilename()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cloud-icon-fixture-loader-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "b.icon.json"), JsonSerializer.Serialize(new CloudIconGoldenFixture
            {
                FixtureName = "b",
                Inputs = new CloudIconCompositionInputs { BaseIconDid = 2 },
                ExpectedPngSha256Hex = new string('b', 64),
            }));
            File.WriteAllText(Path.Combine(directory, "a.icon.json"), JsonSerializer.Serialize(new CloudIconGoldenFixture
            {
                FixtureName = "a",
                Inputs = new CloudIconCompositionInputs { BaseIconDid = 1 },
                ExpectedPngSha256Hex = new string('a', 64),
            }));

            var fixtures = CloudGoldenFixtureLoader.LoadFromDirectory<CloudIconGoldenFixture>(directory, "*.icon.json");

            Assert.HasCount(2, fixtures);
            Assert.AreEqual("a", fixtures[0].FixtureName);
            Assert.AreEqual("b", fixtures[1].FixtureName);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
