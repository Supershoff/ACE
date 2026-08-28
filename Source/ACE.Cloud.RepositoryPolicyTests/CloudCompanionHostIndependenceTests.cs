using System.Text.RegularExpressions;

namespace ACE.Cloud.RepositoryPolicyTests;

/// <summary>
/// Red -> Green test for issue #18's Red section: "Add architecture tests that fail on companion
/// references to ACE.Server world objects or native-biota mutation repositories" and its outcome
/// text: "pure shared contracts... and no live ACE world-object coupling." Walks each companion
/// host's actual <c>&lt;ProjectReference&gt;</c> graph on disk rather than the compiled assembly's
/// metadata: the C# compiler prunes an AssemblyRef entry for any referenced assembly a project never
/// actually uses a type from, so an idle-but-present <c>ProjectReference</c> to ACE.Server would not
/// show up in <see cref="System.Reflection.Assembly.GetReferencedAssemblies"/> at all, even though it
/// is exactly the coupling risk this test exists to catch (a future change could start using an
/// ACE.Server type at any time without the project reference itself changing).
/// </summary>
[TestClass]
public sealed class CloudCompanionHostIndependenceTests
{
    private static readonly string[] CompanionHostProjectNames =
    [
        "ACE.Cloud.Backend",
        "ACE.Cloud.AuthBridge",
        "ACE.Cloud.Worker",
        "ACE.Cloud.Hosting",
    ];

    private static readonly string[] ForbiddenProjectNames = ["ACE.Server", "ACE.Database"];

    private static readonly Regex ProjectReferencePattern =
        new("""<ProjectReference\s+Include="([^"]+)"\s*/>""", RegexOptions.Compiled);

    [TestMethod]
    public void CompanionHosts_NeverTransitivelyReferenceAceServerOrAceDatabase()
    {
        var sourceDirectory = FindSourceDirectory();

        foreach (var hostProjectName in CompanionHostProjectNames)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reached = new List<string>();

            WalkProjectReferences(sourceDirectory, hostProjectName, visited, reached);

            foreach (var forbidden in ForbiddenProjectNames)
            {
                Assert.IsFalse(
                    reached.Contains(forbidden, StringComparer.OrdinalIgnoreCase),
                    $"ARCH-003/ARCH-004: {hostProjectName}'s ProjectReference graph must never reach {forbidden} (path: "
                        + $"{string.Join(" -> ", visited)}) -- that would couple a companion service to live ACE world "
                        + "objects or native-biota mutation repositories. Only ACE.Server's own World Boundary Authority "
                        + "code (CloudCustodyBoundary and friends) may touch those.");
            }
        }
    }

    private static void WalkProjectReferences(string sourceDirectory, string projectName, HashSet<string> visited, List<string> reached)
    {
        if (!visited.Add(projectName))
        {
            return;
        }

        reached.Add(projectName);

        var csprojPath = Path.Combine(sourceDirectory, projectName, projectName + ".csproj");
        if (!File.Exists(csprojPath))
        {
            throw new FileNotFoundException($"Expected to find {csprojPath}; is {projectName} still named/located as this test expects?");
        }

        var contents = File.ReadAllText(csprojPath);

        foreach (Match match in ProjectReferencePattern.Matches(contents))
        {
            // Every ProjectReference in this repository is an MSBuild-style Windows-separator
            // relative path (e.g. "..\ACE.Cloud.Domain\ACE.Cloud.Domain.csproj"), which
            // Path.GetFileNameWithoutExtension would not split correctly on a non-Windows build
            // agent since '\' is not that platform's directory separator.
            var referencePath = match.Groups[1].Value.Replace('\\', '/');
            var referencedProjectName = Path.GetFileNameWithoutExtension(referencePath);
            WalkProjectReferences(sourceDirectory, referencedProjectName, visited, reached);
        }
    }

    private static string FindSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.Name == "Source" && File.Exists(Path.Combine(directory.FullName, "ACE.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Unable to locate the Source directory above {AppContext.BaseDirectory}.");
    }
}
