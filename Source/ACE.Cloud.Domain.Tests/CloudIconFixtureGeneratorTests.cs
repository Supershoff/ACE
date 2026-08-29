using System.Security.Cryptography;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Issue #28's Red requirement: "Add failing tests for the operator fixture-preparation path before
/// implementing it; the operator must not have to hand-author the fixture contracts." Proves
/// <see cref="CloudIconFixtureGenerator"/> turns selected composition inputs plus a trusted reference
/// PNG (or its already-computed hash) into the exact <see cref="CloudIconGoldenFixture"/> contract
/// <see cref="CloudGoldenFixtureLoader"/>/<see cref="CloudIconGoldenComparisonHarness"/> already expect,
/// without the operator ever writing the fixture JSON, computing the hash, or leaking the reference
/// PNG's bytes or filesystem path into the result.
/// </summary>
[TestClass]
public sealed class CloudIconFixtureGeneratorTests
{
    private static readonly byte[] MinimalPngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // an IHDR-shaped chunk is not required for this test;
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // only the leading signature bytes are validated.
    ];

    [TestMethod]
    public void GenerateFromReferencePng_ComputesTheExpectedHash_FromThePngBytes()
    {
        var directory = CreateTempDirectory();
        try
        {
            var pngPath = Path.Combine(directory, "reference.png");
            File.WriteAllBytes(pngPath, MinimalPngBytes);
            var expectedHash = Convert.ToHexStringLower(SHA256.HashData(MinimalPngBytes));

            var fixture = CloudIconFixtureGenerator.GenerateFromReferencePng(
                "clothing-palette-variant-01",
                new CloudIconCompositionInputs { BaseIconDid = 0x06000010, PaletteTemplate = 3, Shade = 0.5f },
                pngPath);

            Assert.AreEqual("clothing-palette-variant-01", fixture.FixtureName);
            Assert.AreEqual(0x06000010u, fixture.Inputs.BaseIconDid);
            Assert.AreEqual(expectedHash, fixture.ExpectedPngSha256Hex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void GenerateFromReferencePng_NeverEmbedsTheReferencePngsAbsolutePath()
    {
        var directory = CreateTempDirectory();
        try
        {
            var pngPath = Path.Combine(directory, "reference.png");
            File.WriteAllBytes(pngPath, MinimalPngBytes);

            var fixture = CloudIconFixtureGenerator.GenerateFromReferencePng(
                "underlay-variant", new CloudIconCompositionInputs { BaseIconDid = 1 }, pngPath);

            var json = System.Text.Json.JsonSerializer.Serialize(fixture);
            Assert.IsFalse(json.Contains(directory, StringComparison.Ordinal), "The fixture must not embed the reference PNG's directory.");
            Assert.IsFalse(json.Contains("reference.png", StringComparison.Ordinal), "The fixture must not embed the reference PNG's file name.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void GenerateFromReferencePng_MissingFile_Throws()
    {
        Assert.ThrowsExactly<FileNotFoundException>(() => CloudIconFixtureGenerator.GenerateFromReferencePng(
            "a", new CloudIconCompositionInputs(), Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png")));
    }

    [TestMethod]
    public void GenerateFromReferencePng_NotAPng_Throws()
    {
        var directory = CreateTempDirectory();
        try
        {
            var notPngPath = Path.Combine(directory, "not-a-png.png");
            File.WriteAllBytes(notPngPath, [0x00, 0x01, 0x02, 0x03]);

            Assert.ThrowsExactly<InvalidDataException>(() => CloudIconFixtureGenerator.GenerateFromReferencePng(
                "a", new CloudIconCompositionInputs(), notPngPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void GenerateFromHash_AcceptsAnOperatorSuppliedHash_WithoutTouchingTheFilesystem()
    {
        var hash = new string('c', 64);

        var fixture = CloudIconFixtureGenerator.GenerateFromHash("stack-count-parity", new CloudIconCompositionInputs { BaseIconDid = 7 }, hash);

        Assert.AreEqual(hash, fixture.ExpectedPngSha256Hex);
    }

    [TestMethod]
    public void GenerateFromHash_NotSixtyFourHexCharacters_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudIconFixtureGenerator.GenerateFromHash("a", new CloudIconCompositionInputs(), "not-a-hash"));
    }

    [TestMethod]
    [DataRow("with/slash")]
    [DataRow("with\\backslash")]
    [DataRow("../escaping")]
    public void GenerateFromHash_FixtureNameLooksLikeAPath_Throws(string fixtureName)
    {
        Assert.ThrowsExactly<ArgumentException>(() => CloudIconFixtureGenerator.GenerateFromHash(fixtureName, new CloudIconCompositionInputs(), new string('a', 64)));
    }

    [TestMethod]
    public async Task GenerateAndWriteAsync_WritesAFixtureThatTheSharedLoaderCanLoadBack()
    {
        var sourceDirectory = CreateTempDirectory();
        var outputDirectory = CreateTempDirectory();
        try
        {
            var pngPath = Path.Combine(sourceDirectory, "reference.png");
            File.WriteAllBytes(pngPath, MinimalPngBytes);

            var outputPath = await CloudIconFixtureGenerator.GenerateAndWriteAsync(
                "round-trip-fixture", new CloudIconCompositionInputs { BaseIconDid = 42 }, pngPath, outputDirectory);

            Assert.AreEqual(Path.Combine(outputDirectory, "round-trip-fixture.icon.json"), outputPath);

            var loaded = CloudGoldenFixtureLoader.LoadFromDirectory<CloudIconGoldenFixture>(outputDirectory, "*.icon.json");
            Assert.HasCount(1, loaded);
            Assert.AreEqual("round-trip-fixture", loaded[0].FixtureName);
            Assert.AreEqual(42u, loaded[0].Inputs.BaseIconDid);

            // The written file must never contain the operator's local directory layout.
            var writtenJson = File.ReadAllText(outputPath);
            Assert.IsFalse(writtenJson.Contains(sourceDirectory, StringComparison.Ordinal), "The written fixture must not embed the reference PNG's directory.");
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cloud-icon-fixture-generator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
