using ACE.Cloud.Contracts;

namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// The Cloud Transaction Authority boundary must refuse mutations from a mismatched Cloud Shard
/// ID or mismatched component versions (ARCH-001, OPS-002).
/// </summary>
[TestClass]
public sealed class CloudProtocolGuardTests
{
    private static readonly CloudShardId DeploymentShardId = new("us1");

    private static readonly CloudComponentVersions ExpectedVersions = new(
        aceExtensionVersion: "1.2.0",
        cloudSchemaVersion: "0.3.0",
        contractProtocolVersion: "2.0.0");

    [TestMethod]
    public void Authorize_MatchingShardAndVersions_IsAuthorized()
    {
        var incoming = new CloudProtocolHandshake(DeploymentShardId, ExpectedVersions with { });

        var result = CloudProtocolGuard.Authorize(DeploymentShardId, ExpectedVersions, incoming);

        Assert.IsTrue(result.IsAuthorized);
        Assert.IsNull(result.Reason);
    }

    [TestMethod]
    public void Authorize_DifferentShardId_RefusesTheMutation()
    {
        var incoming = new CloudProtocolHandshake(new CloudShardId("us2"), ExpectedVersions with { });

        var result = CloudProtocolGuard.Authorize(DeploymentShardId, ExpectedVersions, incoming);

        Assert.IsFalse(result.IsAuthorized);
        StringAssert.Contains(result.Reason, "Cloud Shard ID mismatch");
    }

    [TestMethod]
    public void Authorize_MismatchedContractProtocolVersion_RefusesTheMutation()
    {
        var incoming = new CloudProtocolHandshake(DeploymentShardId, ExpectedVersions with { ContractProtocolVersion = "1.0.0" });

        var result = CloudProtocolGuard.Authorize(DeploymentShardId, ExpectedVersions, incoming);

        Assert.IsFalse(result.IsAuthorized);
        StringAssert.Contains(result.Reason, "Contract protocol");
    }
}
