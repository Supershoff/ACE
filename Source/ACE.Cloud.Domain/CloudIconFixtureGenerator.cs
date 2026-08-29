using System.Security.Cryptography;
using System.Text.Json;

namespace ACE.Cloud.Domain;

/// <summary>
/// Issue #28's local-only icon fixture-generation tooling (Green: "Provide documented local-only
/// fixture-generation tooling that prepares <c>*.icon.json</c> from selected composition inputs plus a
/// trusted reference PNG/hash"). An operator supplies the composition inputs they selected (plain DIDs
/// and palette parameters -- public game data, not private art) and either a path to a trusted reference
/// PNG they have independently confirmed is correct, or that PNG's already-computed SHA-256 hash; this
/// type computes/validates the hash and assembles the exact <see cref="CloudIconGoldenFixture"/> shape
/// <see cref="CloudGoldenFixtureLoader"/> and <see cref="CloudIconGoldenComparisonHarness"/> already
/// expect, so the operator never hand-authors the fixture JSON or its hash by hand (issue #28: "the
/// operator must not have to hand-author the fixture contracts"). Only the PNG's content hash is ever
/// retained -- the reference PNG's bytes and its filesystem path are read and discarded, never copied
/// into the generated contract.
/// </summary>
public static class CloudIconFixtureGenerator
{
    /// <summary>Builds a fixture from a trusted reference PNG file, hashing it and discarding its bytes and path.</summary>
    public static CloudIconGoldenFixture GenerateFromReferencePng(string fixtureName, CloudIconCompositionInputs inputs, string referencePngPath)
    {
        if (string.IsNullOrWhiteSpace(referencePngPath))
        {
            throw new ArgumentException("A trusted reference PNG path is required.", nameof(referencePngPath));
        }

        if (!File.Exists(referencePngPath))
        {
            throw new FileNotFoundException("The trusted reference PNG was not found.", referencePngPath);
        }

        var pngBytes = File.ReadAllBytes(referencePngPath);
        ValidatePngSignature(pngBytes, referencePngPath);

        var hash = Convert.ToHexStringLower(SHA256.HashData(pngBytes));
        return GenerateFromHash(fixtureName, inputs, hash);
    }

    /// <summary>Builds a fixture from an already-computed reference hash, for an operator who hashed the PNG themselves.</summary>
    public static CloudIconGoldenFixture GenerateFromHash(string fixtureName, CloudIconCompositionInputs inputs, string expectedPngSha256Hex)
    {
        CloudFixtureContractSanitizer.ValidateFixtureName(fixtureName, nameof(fixtureName));
        ArgumentNullException.ThrowIfNull(inputs);
        ValidateSha256Hex(expectedPngSha256Hex, nameof(expectedPngSha256Hex));

        var fixture = new CloudIconGoldenFixture
        {
            FixtureName = fixtureName,
            Inputs = inputs,
            ExpectedPngSha256Hex = expectedPngSha256Hex.ToLowerInvariant(),
        };

        CloudFixtureContractSanitizer.EnsureNoAbsolutePath(JsonSerializer.Serialize(fixture), fixtureName);
        return fixture;
    }

    /// <summary>Generates a fixture from a trusted reference PNG and writes it as <c>{fixtureName}.icon.json</c> under <paramref name="outputDirectory"/>.</summary>
    public static async Task<string> GenerateAndWriteAsync(
        string fixtureName, CloudIconCompositionInputs inputs, string referencePngPath, string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var fixture = GenerateFromReferencePng(fixtureName, inputs, referencePngPath);
        return await WriteAsync(fixture, outputDirectory, cancellationToken);
    }

    /// <summary>Writes an already-built fixture (e.g. from <see cref="GenerateFromHash"/>) as <c>{fixture.FixtureName}.icon.json</c> under <paramref name="outputDirectory"/>.</summary>
    public static async Task<string> WriteAsync(CloudIconGoldenFixture fixture, string outputDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        }

        var json = JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true });
        CloudFixtureContractSanitizer.EnsureNoAbsolutePath(json, fixture.FixtureName);

        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, $"{fixture.FixtureName}.icon.json");
        await File.WriteAllTextAsync(outputPath, json, cancellationToken);
        return outputPath;
    }

    private static void ValidateSha256Hex(string hash, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A SHA-256 hash must be exactly 64 hexadecimal characters.", parameterName);
        }
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static void ValidatePngSignature(byte[] bytes, string referencePngPath)
    {
        if (bytes.Length < PngSignature.Length || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            throw new InvalidDataException($"'{Path.GetFileName(referencePngPath)}' is not a valid PNG file (missing PNG signature).");
        }
    }
}
