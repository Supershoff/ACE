namespace ACE.Cloud.Domain.Tests;

/// <summary>
/// OPS-002: a Cloud boundary mutation must be refused, with the exact incompatible component
/// named, whenever the ACE extension, Cloud schema, or contract protocol versions differ.
/// </summary>
[TestClass]
public sealed class CloudCompatibilityCheckerTests
{
    private static readonly CloudComponentVersions Expected = new(
        aceExtensionVersion: "1.2.0",
        cloudSchemaVersion: "0.3.0",
        contractProtocolVersion: "2.0.0");

    [TestMethod]
    public void Evaluate_MatchingVersions_IsCompatible()
    {
        var actual = Expected with { };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual);

        Assert.IsTrue(result.IsCompatible);
        Assert.IsNull(result.IncompatibleComponent);
        Assert.IsNull(result.Reason);
    }

    [TestMethod]
    public void Evaluate_MismatchedAceExtensionVersion_IsRejected()
    {
        var actual = Expected with { AceExtensionVersion = "1.1.0" };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual);

        Assert.IsFalse(result.IsCompatible);
        Assert.AreEqual(CloudVersionComponent.AceExtension, result.IncompatibleComponent);
        StringAssert.Contains(result.Reason, "ACE extension");
    }

    [TestMethod]
    public void Evaluate_MismatchedCloudSchemaVersion_IsRejected()
    {
        var actual = Expected with { CloudSchemaVersion = "0.2.0" };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual);

        Assert.IsFalse(result.IsCompatible);
        Assert.AreEqual(CloudVersionComponent.CloudSchema, result.IncompatibleComponent);
        StringAssert.Contains(result.Reason, "Cloud schema");
    }

    [TestMethod]
    public void Evaluate_MismatchedContractProtocolVersion_IsRejected()
    {
        var actual = Expected with { ContractProtocolVersion = "1.0.0" };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual);

        Assert.IsFalse(result.IsCompatible);
        Assert.AreEqual(CloudVersionComponent.ContractProtocol, result.IncompatibleComponent);
        StringAssert.Contains(result.Reason, "Contract protocol");
    }

    [TestMethod]
    public void Evaluate_AceExtensionMismatchTakesPriorityOverOtherMismatches()
    {
        var actual = Expected with
        {
            AceExtensionVersion = "1.1.0",
            CloudSchemaVersion = "0.2.0",
            ContractProtocolVersion = "1.0.0",
        };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual);

        Assert.AreEqual(CloudVersionComponent.AceExtension, result.IncompatibleComponent);
    }
}
