using ACE.Cloud.Domain;
using ACE.Cloud.Worker;

namespace ACE.Cloud.Worker.Tests;

/// <summary>
/// Issue #28's local-only fixture-generation CLI (see <see cref="CloudFixtureGeneratorCli"/> and
/// <c>docs/agents/fidelity-phase-gate.md</c>): proves the documented commands actually generate and
/// validate fixture contracts end to end without the operator hand-authoring the fixture JSON, and that
/// bad input is rejected with a non-zero exit code rather than silently producing an invalid fixture.
/// </summary>
[TestClass]
public sealed class CloudFixtureGeneratorCliTests
{
    [TestMethod]
    public async Task GenerateIconFixture_WithReferenceHash_WritesALoadableFixture()
    {
        var outputDirectory = CreateTempDirectory();
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await CloudFixtureGeneratorCli.RunAsync(
            [
                "generate-icon-fixture",
                "--fixture-name", "cli-icon-fixture",
                "--inputs", "{\"BaseIconDid\":100690954,\"PaletteTemplate\":12}",
                "--reference-hash", new string('a', 64),
                "--output-dir", outputDirectory,
            ],
            output, error);

            Assert.AreEqual(0, exitCode, error.ToString());
            var loaded = CloudGoldenFixtureLoader.LoadFromDirectory<CloudIconGoldenFixture>(outputDirectory, "*.icon.json");
            Assert.HasCount(1, loaded);
            Assert.AreEqual("cli-icon-fixture", loaded[0].FixtureName);
            Assert.AreEqual(100690954u, loaded[0].Inputs.BaseIconDid);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GenerateIconFixture_BothReferencePngAndReferenceHash_FailsRatherThanPickingOne()
    {
        var outputDirectory = CreateTempDirectory();
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await CloudFixtureGeneratorCli.RunAsync(
            [
                "generate-icon-fixture",
                "--fixture-name", "ambiguous",
                "--inputs", "{}",
                "--reference-hash", new string('a', 64),
                "--reference-png", "some.png",
                "--output-dir", outputDirectory,
            ],
            output, error);

            Assert.AreEqual(1, exitCode);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task GenerateAppraisalFixture_DerivesThePanel_WithoutTheOperatorAuthoringIt()
    {
        var outputDirectory = CreateTempDirectory();
        try
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = await CloudFixtureGeneratorCli.RunAsync(
            [
                "generate-appraisal-fixture",
                "--fixture-name", "cli-appraisal-fixture",
                "--capture", "{\"ItemId\":1,\"Name\":\"CLI Test Item\"}",
                "--output-dir", outputDirectory,
            ],
            output, error);

            Assert.AreEqual(0, exitCode, error.ToString());
            var loaded = CloudGoldenFixtureLoader.LoadFromDirectory<CloudAppraisalGoldenFixture>(outputDirectory, "*.appraisal.json");
            Assert.HasCount(1, loaded);
            Assert.AreEqual("CLI Test Item", loaded[0].ExpectedPanel.ItemName);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ValidateFixture_GeneratorProducedIconFixture_ExitsZero()
    {
        var outputDirectory = CreateTempDirectory();
        try
        {
            var generated = await CloudFixtureGeneratorCli.RunAsync(
            [
                "generate-icon-fixture",
                "--fixture-name", "valid-fixture",
                "--inputs", "{}",
                "--reference-hash", new string('a', 64),
                "--output-dir", outputDirectory,
            ],
            new StringWriter(), new StringWriter());
            Assert.AreEqual(0, generated);

            var output = new StringWriter();
            var exitCode = await CloudFixtureGeneratorCli.RunAsync(
                ["validate-fixture", Path.Combine(outputDirectory, "valid-fixture.icon.json")], output, new StringWriter());

            Assert.AreEqual(0, exitCode);
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ValidateFixture_MalformedHash_ExitsNonZeroAndReportsTheProblem()
    {
        var directory = CreateTempDirectory();
        try
        {
            var path = Path.Combine(directory, "bad.icon.json");
            File.WriteAllText(path, "{\"FixtureName\":\"bad\",\"Inputs\":{},\"ExpectedPngSha256Hex\":\"too-short\"}");

            var output = new StringWriter();
            var exitCode = await CloudFixtureGeneratorCli.RunAsync(["validate-fixture", path], output, new StringWriter());

            Assert.AreEqual(1, exitCode);
            Assert.IsGreaterThan(0, output.ToString().Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task UnknownCommand_ExitsNonZero()
    {
        var error = new StringWriter();
        var exitCode = await CloudFixtureGeneratorCli.RunAsync(["not-a-real-command"], new StringWriter(), error);

        Assert.AreEqual(1, exitCode);
        Assert.IsGreaterThan(0, error.ToString().Length);
    }

    [TestMethod]
    public async Task GenerateIconFixture_MissingRequiredOption_ExitsNonZero()
    {
        var error = new StringWriter();
        var exitCode = await CloudFixtureGeneratorCli.RunAsync(
            ["generate-icon-fixture", "--fixture-name", "a"], new StringWriter(), error);

        Assert.AreEqual(1, exitCode);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cloud-fixture-generator-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
