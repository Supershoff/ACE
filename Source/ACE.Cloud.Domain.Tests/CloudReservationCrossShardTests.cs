using ACE.Cloud.Contracts;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// Cross-shard use is an illegal transition (ARCH-001, Red section), but the rule already exists at
/// the command boundary (<see cref="CloudCommandGuard"/>, issue #6) rather than inside
/// <see cref="CloudReservationPolicy"/> itself: a command whose handshake targets the wrong Cloud
/// Shard must be rejected before any reservation state machine ever runs, so
/// <see cref="CloudReservationPolicy.Open"/> is never even reached with cross-shard targets.
/// </summary>
[TestClass]
public sealed class CloudReservationCrossShardTests
{
    private static readonly CloudShardId DeploymentShardId = new("us1");

    private static readonly CloudComponentVersions Versions = new(
        aceExtensionVersion: "1.2.0", cloudSchemaVersion: "0.3.0", contractProtocolVersion: "2.0.0");

    [TestMethod]
    public void AWithdrawalReservationCommand_FromAnotherShard_NeverReachesTheReservationStateMachine()
    {
        var otherShardHandshake = new CloudProtocolHandshake(new CloudShardId("us2"), Versions with { });
        var command = new CloudWithdrawalReservationCommand(
            new CloudItemId(1), new CloudAccountId(Guid.NewGuid()), new CloudReservationId(Guid.NewGuid()));
        var envelope = new CloudCommandEnvelope<CloudWithdrawalReservationCommand>(
            otherShardHandshake, new CloudIdempotencyKey(Guid.NewGuid()), CloudActorIdentity.SystemActor("Test"), command,
            DateTimeOffset.UtcNow);

        var precondition = CloudCommandGuard.Evaluate(envelope, DeploymentShardId, Versions, currentAggregateVersion: null);

        Assert.IsFalse(precondition.Passed);
        Assert.AreEqual(CloudCommandResultKind.ValidationFailed, precondition.FailureKind);
        StringAssert.Contains(precondition.Reason, "Cloud Shard ID mismatch");

        // Because the precondition failed, a correctly composed caller stops here: it never calls
        // CloudReservationPolicy.Open with this envelope's command at all.
    }

    [TestMethod]
    public void AWithdrawalReservationCommand_FromTheBoundShard_PassesThePreconditionAndMayProceed()
    {
        var sameShardHandshake = new CloudProtocolHandshake(DeploymentShardId, Versions with { });
        var command = new CloudWithdrawalReservationCommand(
            new CloudItemId(1), new CloudAccountId(Guid.NewGuid()), new CloudReservationId(Guid.NewGuid()));
        var envelope = new CloudCommandEnvelope<CloudWithdrawalReservationCommand>(
            sameShardHandshake, new CloudIdempotencyKey(Guid.NewGuid()), CloudActorIdentity.SystemActor("Test"), command,
            DateTimeOffset.UtcNow);

        var precondition = CloudCommandGuard.Evaluate(envelope, DeploymentShardId, Versions, currentAggregateVersion: null);

        Assert.IsTrue(precondition.Passed);

        var opened = CloudReservationPolicy.Open(
            command.ReservationId, CloudReservationKind.Withdrawal, command.OwnerId, [CloudReservationTarget.ForItem(command.ItemId)],
            new Dictionary<CloudReservationTarget, CloudReservationAllocation>(), DateTimeOffset.UtcNow, CloudMutationGateState.Open);

        Assert.IsTrue(opened.IsSuccess);
    }
}
