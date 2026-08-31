namespace ACE.Cloud.RepositoryPolicyTests;

/// <summary>
/// Code-review regression test: <c>ACE.Cloud.Persistence.CloudNotificationProjectionConsumer.RunBatchAsync</c>
/// is the only code path that turns a Custody Outbox event into a Notification Center row and a
/// "Notification" Live State Stream event (EVT-003, EVT-007). Before this test existed, no hosted
/// service ever invoked it in production -- <c>ACE.Cloud.Worker/Program.cs</c> registered only the
/// custody and identity projection consumer workers, so every real notification-worthy event sat in
/// the Custody Outbox forever as far as the Notification Center was concerned, even though every unit
/// test exercising the consumer instantiated it directly. Building the real <c>Program</c> host here
/// would require a live MariaDB connection and would block forever in <c>host.Run()</c>, so this walks
/// the actual worker source on disk instead -- the same pragmatic approach
/// <see cref="CloudCompanionHostIndependenceTests"/> already uses for a different wiring invariant in
/// this same host.
/// </summary>
[TestClass]
public sealed class CloudNotificationProjectionConsumerWorkerRegistrationTests
{
    [TestMethod]
    public void CloudNotificationProjectionConsumerWorker_ExistsAndMirrorsTheOtherProjectionConsumerWorkers()
    {
        var workerDirectory = Path.Combine(FindSourceDirectory(), "ACE.Cloud.Worker");
        var workerFilePath = Path.Combine(workerDirectory, "CloudNotificationProjectionConsumerWorker.cs");

        Assert.IsTrue(
            File.Exists(workerFilePath),
            "EVT-003/EVT-007: ACE.Cloud.Worker must contain a CloudNotificationProjectionConsumerWorker.cs "
                + "(mirroring CloudCustodyProjectionConsumerWorker.cs / CloudIdentityProjectionConsumerWorker.cs), "
                + "the BackgroundService that actually invokes CloudNotificationProjectionConsumer.RunBatchAsync.");

        var workerSource = File.ReadAllText(workerFilePath);
        StringAssert.Contains(
            workerSource,
            "BackgroundService",
            "CloudNotificationProjectionConsumerWorker.cs must be a BackgroundService, matching the other projection consumer workers' polling shape.");
        StringAssert.Contains(
            workerSource,
            "CloudNotificationProjectionConsumer(",
            "CloudNotificationProjectionConsumerWorker must actually construct and invoke CloudNotificationProjectionConsumer.");
    }

    [TestMethod]
    public void Program_RegistersTheNotificationProjectionConsumerWorkerAsAHostedService()
    {
        var programFilePath = Path.Combine(FindSourceDirectory(), "ACE.Cloud.Worker", "Program.cs");
        var programSource = File.ReadAllText(programFilePath);

        StringAssert.Contains(
            programSource,
            "AddHostedService<CloudNotificationProjectionConsumerWorker>()",
            "ACE.Cloud.Worker/Program.cs must register CloudNotificationProjectionConsumerWorker as a hosted "
                + "service next to CloudCustodyProjectionConsumerWorker/CloudIdentityProjectionConsumerWorker -- "
                + "otherwise the Notification Center never receives any events in production.");
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
