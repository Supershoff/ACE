using ACE.Cloud.Contracts;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// The Cloud Transaction Authority boundary must refuse a command envelope with a mismatched
/// Cloud Shard ID or unsupported protocol version (ARCH-001, OPS-002) and must report a stale
/// expected aggregate version as a conflict rather than silently applying it (ARCH-006,
/// transaction rule 3) before any business rule runs.
/// </summary>
[TestClass]
public sealed class CloudCommandGuardTests
{
    private static readonly CloudShardId DeploymentShardId = new("us1");

    private static readonly CloudComponentVersions ExpectedVersions = new(
        aceExtensionVersion: "1.2.0",
        cloudSchemaVersion: "0.3.0",
        contractProtocolVersion: "2.0.0");

    private static CloudCommandEnvelope<string> CreateEnvelope(
        CloudShardId? shardId = null,
        CloudComponentVersions? versions = null,
        CloudAggregateVersion? expectedVersion = null)
    {
        var handshake = new CloudProtocolHandshake(shardId ?? DeploymentShardId, versions ?? ExpectedVersions with { });

        return new CloudCommandEnvelope<string>(
            handshake,
            new CloudIdempotencyKey(Guid.NewGuid()),
            CloudActorIdentity.SystemActor("Test"),
            "command-payload",
            DateTimeOffset.UtcNow,
            expectedVersion);
    }

    [TestMethod]
    public void Evaluate_MatchingHandshakeAndCurrentVersion_Passes()
    {
        var envelope = CreateEnvelope(expectedVersion: new CloudAggregateVersion(3));

        var result = CloudCommandGuard.Evaluate(envelope, DeploymentShardId, ExpectedVersions, new CloudAggregateVersion(3));

        Assert.IsTrue(result.Passed);
        Assert.IsNull(result.FailureKind);
        Assert.IsNull(result.Reason);
    }

    [TestMethod]
    public void Evaluate_NoExpectedVersion_PassesRegardlessOfCurrentVersion()
    {
        var envelope = CreateEnvelope();

        var result = CloudCommandGuard.Evaluate(envelope, DeploymentShardId, ExpectedVersions, currentAggregateVersion: null);

        Assert.IsTrue(result.Passed);
    }

    [TestMethod]
    public void Evaluate_MismatchedShardId_FailsAsValidation()
    {
        var envelope = CreateEnvelope(shardId: new CloudShardId("us2"));

        var result = CloudCommandGuard.Evaluate(envelope, DeploymentShardId, ExpectedVersions, currentAggregateVersion: null);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual(CloudCommandResultKind.ValidationFailed, result.FailureKind);
        StringAssert.Contains(result.Reason, "Cloud Shard ID mismatch");
    }

    [TestMethod]
    public void Evaluate_UnsupportedContractProtocolVersion_FailsAsValidation()
    {
        var envelope = CreateEnvelope(versions: ExpectedVersions with { ContractProtocolVersion = "1.0.0" });

        var result = CloudCommandGuard.Evaluate(envelope, DeploymentShardId, ExpectedVersions, currentAggregateVersion: null);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual(CloudCommandResultKind.ValidationFailed, result.FailureKind);
        StringAssert.Contains(result.Reason, "Contract protocol");
    }

    [TestMethod]
    public void Evaluate_StaleExpectedVersion_ReturnsConflict()
    {
        var envelope = CreateEnvelope(expectedVersion: new CloudAggregateVersion(2));

        var result = CloudCommandGuard.Evaluate(envelope, DeploymentShardId, ExpectedVersions, new CloudAggregateVersion(5));

        Assert.IsFalse(result.Passed);
        Assert.AreEqual(CloudCommandResultKind.Conflict, result.FailureKind);
        StringAssert.Contains(result.Reason, "Expected aggregate version 2");
        StringAssert.Contains(result.Reason, "current version is 5");
    }

    [TestMethod]
    public void Evaluate_ExpectedVersionAgainstMissingAggregate_ReturnsConflict()
    {
        var envelope = CreateEnvelope(expectedVersion: new CloudAggregateVersion(1));

        var result = CloudCommandGuard.Evaluate(envelope, DeploymentShardId, ExpectedVersions, currentAggregateVersion: null);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual(CloudCommandResultKind.Conflict, result.FailureKind);
    }

    [TestMethod]
    public void FailedPrecondition_ToFailureResult_ProducesMatchingCommandResultKind()
    {
        var envelope = CreateEnvelope(expectedVersion: new CloudAggregateVersion(2));

        var precondition = CloudCommandGuard.Evaluate(envelope, DeploymentShardId, ExpectedVersions, new CloudAggregateVersion(5));
        var result = precondition.ToFailureResult<string>();

        Assert.AreEqual(CloudCommandResultKind.Conflict, result.Kind);
        Assert.AreEqual(precondition.Reason, result.Reason);
    }

    [TestMethod]
    public void PassingPrecondition_ToFailureResult_Throws()
    {
        var precondition = CloudCommandPreconditionResult.Ok();

        Assert.ThrowsExactly<InvalidOperationException>(() => precondition.ToFailureResult<string>());
    }
}
