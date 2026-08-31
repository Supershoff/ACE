namespace ACE.Cloud.RepositoryPolicyTests;

/// <summary>
/// Human-acceptance regression (issue #34): "<c>CloudIconCompositionCache.GetOrComposeAsync</c> is
/// only exercised by tests/fixture tooling; no runtime producer schedules deposited/backfilled item
/// composition or writes the resulting cache key." Before this test existed, every deposited item
/// kept its neutral fallback glyph forever, since nothing but a unit test or the local fixture
/// generator ever called <c>GetOrComposeAsync</c>. Mirrors
/// <see cref="CloudNotificationProjectionConsumerWorkerRegistrationTests"/>'s source-scanning
/// approach: building the real <c>Program</c> host would require a live MariaDB connection and staged
/// DAT bytes and would block forever in <c>host.Run()</c>.
/// </summary>
[TestClass]
public sealed class CloudIconCompositionWorkerRegistrationTests
{
    [TestMethod]
    public void CloudIconCompositionWorker_ExistsAndActuallyInvokesTheCompositionCache()
    {
        var workerDirectory = Path.Combine(FindSourceDirectory(), "ACE.Cloud.Worker");
        var workerFilePath = Path.Combine(workerDirectory, "CloudIconCompositionWorker.cs");

        Assert.IsTrue(
            File.Exists(workerFilePath),
            "ACE.Cloud.Worker must contain a CloudIconCompositionWorker.cs (mirroring "
                + "CloudNotificationProjectionConsumerWorker.cs), the BackgroundService that actually invokes "
                + "CloudIconCompositionCache.GetOrComposeAsync and writes IconCacheKeyHex back.");

        var workerSource = File.ReadAllText(workerFilePath);
        StringAssert.Contains(
            workerSource,
            "BackgroundService",
            "CloudIconCompositionWorker.cs must be a BackgroundService, matching the other worker polling loops.");
        StringAssert.Contains(
            workerSource,
            "GetOrComposeAsync(",
            "CloudIconCompositionWorker must actually call CloudIconCompositionCache.GetOrComposeAsync.");
        StringAssert.Contains(
            workerSource,
            "IconCacheKeyHex",
            "CloudIconCompositionWorker must write the composed cache key back through CloudInventoryItemPropertiesGateway.");
    }

    [TestMethod]
    public void Program_RegistersTheIconCompositionWorkerAsAHostedService()
    {
        var programFilePath = Path.Combine(FindSourceDirectory(), "ACE.Cloud.Worker", "Program.cs");
        var programSource = File.ReadAllText(programFilePath);

        StringAssert.Contains(
            programSource,
            "AddHostedService<CloudIconCompositionWorker>()",
            "ACE.Cloud.Worker/Program.cs must register CloudIconCompositionWorker as a hosted service -- "
                + "otherwise deposited items never get a real composed icon in production.");
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
