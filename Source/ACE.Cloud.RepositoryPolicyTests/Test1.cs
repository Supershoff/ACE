using System.Diagnostics;

namespace ACE.Cloud.RepositoryPolicyTests;

/// <summary>
/// Proves the repository's own ignore rules keep private deployment data out of version control:
/// DAT client assets, generated build output, operator secrets, and operator configuration.
/// This complements, but does not replace, Cloud Mule CI's tracked-file policy check, which
/// covers only the current pull request's diff rather than the ignore rules themselves.
/// </summary>
[TestClass]
public sealed class RepositoryIgnorePolicyTests
{
    [TestMethod]
    [DataRow("Dats/client_portal.dat", DisplayName = "DAT client assets")]
    [DataRow("Source/ACE.Cloud.Domain/bin/Debug/net10.0/ACE.Cloud.Domain.dll", DisplayName = "Generated build output")]
    [DataRow("Database/tools/password.txt", DisplayName = "Operator secrets")]
    [DataRow("Config/Config.js", DisplayName = "Operator configuration")]
    public void SamplePath_IsIgnoredByGit(string samplePath)
    {
        var (exitCode, output) = RunGit("check-ignore", "-v", samplePath);

        Assert.AreEqual(
            0,
            exitCode,
            $"'{samplePath}' must be covered by a .gitignore rule so it cannot be committed accidentally. git output: {output}");
    }

    private static (int ExitCode, string Output) RunGit(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the git process.");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException($"Unable to locate the repository root (.git) above {AppContext.BaseDirectory}.");
    }
}
