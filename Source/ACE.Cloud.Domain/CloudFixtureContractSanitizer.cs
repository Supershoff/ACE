using System.Text.RegularExpressions;

namespace ACE.Cloud.Domain;

/// <summary>
/// Shared guard for issue #28's fixture-generation tooling (Green: "Validate and sanitize generated
/// contracts; never copy private art, capture bytes, DAT content, secrets, or absolute operator paths
/// into shareable evidence."). Applied to every generated <c>*.icon.json</c>/<c>*.appraisal.json</c>
/// fixture contract -- and to a fixture name before it is ever used to build an output file path -- so
/// an operator's local directory layout can never leak into a fixture that later gets attached to a
/// GitHub issue by mistake.
/// </summary>
public static class CloudFixtureContractSanitizer
{
    /// <summary>
    /// Recognizable absolute-path prefixes across the platforms this tooling runs on: Windows drive
    /// letters and UNC roots, and the POSIX directories real operator DAT/capture corpora live under.
    /// Matches the forbidden-substring list <c>CloudFidelityPhaseGateReportTests.SerializedReport_NeverContainsAnAbsoluteFilesystemPath</c>
    /// already asserts against, so both guards stay in lockstep.
    /// </summary>
    private static readonly Regex AbsolutePathPattern = new(
        @"([A-Za-z]:\\|\\\\|/home/|/Users/|/var/|/tmp/|/etc/|/root/)", RegexOptions.Compiled);

    public static bool ContainsAbsolutePath(string text) =>
        !string.IsNullOrEmpty(text) && AbsolutePathPattern.IsMatch(text);

    /// <summary>
    /// A fixture name becomes part of an output file name (<c>{FixtureName}.icon.json</c>); rejecting
    /// path separators and drive-letter/UNC prefixes here keeps a generated fixture from ever escaping
    /// its intended output directory or embedding a directory layout in its own file name.
    /// </summary>
    public static void ValidateFixtureName(string fixtureName, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(fixtureName))
        {
            throw new ArgumentException("A fixture name is required.", parameterName);
        }

        if (fixtureName.IndexOfAny(['/', '\\']) >= 0 || ContainsAbsolutePath(fixtureName) || fixtureName.Contains(".."))
        {
            throw new ArgumentException(
                $"Fixture name '{fixtureName}' must be a plain name, not a path -- it must not contain '/', '\\', or '..'.",
                parameterName);
        }
    }

    /// <summary>
    /// Final check before writing a generated fixture's JSON to disk: refuses to write if the
    /// serialized contract embeds an absolute path from the operator's own machine.
    /// </summary>
    public static void EnsureNoAbsolutePath(string serializedFixtureJson, string fixtureName)
    {
        if (ContainsAbsolutePath(serializedFixtureJson))
        {
            throw new InvalidOperationException(
                $"Refusing to write fixture '{fixtureName}': its generated contract appears to embed an absolute filesystem path.");
        }
    }
}
