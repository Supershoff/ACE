using System.Text.Json;
using ACE.Cloud.Domain;

namespace ACE.Cloud.Worker;

/// <summary>
/// Issue #28's local-only command-line entry point for the fixture-generation tooling (Green:
/// "Provide documented local-only fixture-generation tooling that prepares <c>*.icon.json</c>... and
/// prepares <c>*.appraisal.json</c>..."; acceptance criteria: "A documented local-only command
/// generates/validates the icon and appraisal fixture contracts without requiring the operator to
/// hand-author JSON or exposing private inputs."). <see cref="Program"/> only ever dispatches here when
/// the first command-line argument names one of <see cref="KnownCommands"/>, so ordinary hosted-worker
/// startup (no arguments, or hosting/config switches) is completely unaffected. Documented end to end in
/// <c>docs/agents/fidelity-phase-gate.md</c>.
///
/// All I/O this type performs stays on the operator's own machine: it reads a trusted reference PNG or
/// an operator-owned capture and writes one local <c>*.icon.json</c>/<c>*.appraisal.json</c> file, and
/// never itself uploads, prints, or otherwise shares a fixture's content -- the fixture files this
/// produces are the "documented local-only fixture-generation tooling" the issue asks for, never
/// shareable evidence on their own (only the redacted <c>CloudFidelityPhaseGateReport</c> a harness run
/// against them later produces is safe to attach anywhere).
/// </summary>
public static class CloudFixtureGeneratorCli
{
    public static readonly IReadOnlyList<string> KnownCommands =
        ["generate-icon-fixture", "generate-appraisal-fixture", "validate-fixture"];

    private static readonly IReadOnlyList<string> IconFixtureFlags =
        ["fixture-name", "inputs", "output-dir", "reference-png", "reference-hash"];

    private static readonly IReadOnlyList<string> AppraisalFixtureFlags =
        ["fixture-name", "capture", "output-dir"];

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0)
        {
            error.WriteLine("A command is required. Known commands: " + string.Join(", ", KnownCommands));
            return 1;
        }

        try
        {
            return args[0] switch
            {
                "generate-icon-fixture" => await RunGenerateIconFixtureAsync(args[1..], output),
                "generate-appraisal-fixture" => await RunGenerateAppraisalFixtureAsync(args[1..], output),
                "validate-fixture" => RunValidateFixture(args[1..], output),
                _ => Unknown(args[0], error),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or InvalidDataException or InvalidOperationException or JsonException)
        {
            error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string command, TextWriter error)
    {
        error.WriteLine($"Unknown command '{command}'. Known commands: " + string.Join(", ", KnownCommands));
        return 1;
    }

    private static async Task<int> RunGenerateIconFixtureAsync(string[] args, TextWriter output)
    {
        var options = ParseOptions(args, IconFixtureFlags);
        var fixtureName = Require(options, "fixture-name");
        var outputDirectory = Require(options, "output-dir");
        var inputs = JsonSerializer.Deserialize<CloudIconCompositionInputs>(ReadJsonOrLiteral(Require(options, "inputs")))
            ?? throw new ArgumentException("The --inputs value did not deserialize to composition inputs.");

        var hasPng = options.TryGetValue("reference-png", out var referencePngPath);
        var hasHash = options.TryGetValue("reference-hash", out var referenceHash);
        if (hasPng == hasHash)
        {
            throw new ArgumentException("Exactly one of --reference-png or --reference-hash is required.");
        }

        var outputPath = hasPng
            ? await CloudIconFixtureGenerator.GenerateAndWriteAsync(fixtureName, inputs, referencePngPath!, outputDirectory)
            : await CloudIconFixtureGenerator.WriteAsync(CloudIconFixtureGenerator.GenerateFromHash(fixtureName, inputs, referenceHash!), outputDirectory);

        output.WriteLine($"Wrote {outputPath}");
        return 0;
    }

    private static async Task<int> RunGenerateAppraisalFixtureAsync(string[] args, TextWriter output)
    {
        var options = ParseOptions(args, AppraisalFixtureFlags);
        var fixtureName = Require(options, "fixture-name");
        var outputDirectory = Require(options, "output-dir");
        var capturedSnapshot = JsonSerializer.Deserialize<CloudAppraisalRawItemSnapshot>(ReadJsonOrLiteral(Require(options, "capture")))
            ?? throw new ArgumentException("The --capture value did not deserialize to a captured item snapshot.");

        var outputPath = await CloudAppraisalFixtureGenerator.GenerateAndWriteAsync(fixtureName, capturedSnapshot, outputDirectory);

        output.WriteLine($"Wrote {outputPath}");
        return 0;
    }

    private static int RunValidateFixture(string[] args, TextWriter output)
    {
        if (args.Length != 1)
        {
            throw new ArgumentException("validate-fixture requires exactly one argument: the path to a *.icon.json or *.appraisal.json file.");
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Fixture file not found.", path);
        }

        IReadOnlyList<string> problems;
        if (path.EndsWith(".icon.json", StringComparison.OrdinalIgnoreCase))
        {
            var fixture = JsonSerializer.Deserialize<CloudIconGoldenFixture>(File.ReadAllText(path))
                ?? throw new InvalidDataException($"{Path.GetFileName(path)} did not deserialize to an icon fixture.");
            problems = CloudFixtureContractValidator.ValidateIconFixture(fixture);
        }
        else if (path.EndsWith(".appraisal.json", StringComparison.OrdinalIgnoreCase))
        {
            var fixture = JsonSerializer.Deserialize<CloudAppraisalGoldenFixture>(File.ReadAllText(path))
                ?? throw new InvalidDataException($"{Path.GetFileName(path)} did not deserialize to an appraisal fixture.");
            problems = CloudFixtureContractValidator.ValidateAppraisalFixture(fixture);
        }
        else
        {
            throw new ArgumentException("The fixture path must end with .icon.json or .appraisal.json.");
        }

        if (problems.Count == 0)
        {
            output.WriteLine($"{path} is valid.");
            return 0;
        }

        foreach (var problem in problems)
        {
            output.WriteLine($"- {problem}");
        }

        return 1;
    }

    private static Dictionary<string, string> ParseOptions(string[] args, IReadOnlyList<string> knownFlags)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{arg}'. Options must be passed as --name value.");
            }

            var name = arg[2..];
            if (!knownFlags.Contains(name))
            {
                throw new ArgumentException($"Unknown option '--{name}'. Known options: " + string.Join(", ", knownFlags.Select(f => "--" + f)));
            }

            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"Option '--{name}' requires a value.");
            }

            options[name] = args[++i];
        }

        return options;
    }

    private static string Require(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException($"Missing required option --{name}.");

    /// <summary>Reads the option's value as a file's contents if it names an existing local file, otherwise treats the value itself as literal JSON.</summary>
    private static string ReadJsonOrLiteral(string value) => File.Exists(value) ? File.ReadAllText(value) : value;
}
