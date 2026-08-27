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

    // OPS-002: "declare supported ACE releases" and "use versioned forward migrations". A
    // CloudSupportedProtocolWindow lets a deployment accept more than one contract protocol
    // version -- forward compatibility with an older caller, backward compatibility with a newer
    // one -- while the ACE extension and Cloud schema versions still require an exact match.
    private static readonly CloudSupportedProtocolWindow Window = new(
        minimumInclusive: new CloudProtocolVersion(1, 9, 0),
        maximumInclusive: new CloudProtocolVersion(2, 1, 0));

    [TestMethod]
    public void Evaluate_WithProtocolWindow_ContractProtocolVersionAtTheWindowMinimum_IsCompatible()
    {
        var actual = Expected with { ContractProtocolVersion = "1.9.0" };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual, Window);

        Assert.IsTrue(result.IsCompatible, "A caller declaring the oldest protocol version still inside the supported window must be accepted (backward compatibility).");
    }

    [TestMethod]
    public void Evaluate_WithProtocolWindow_ContractProtocolVersionAtTheWindowMaximum_IsCompatible()
    {
        var actual = Expected with { ContractProtocolVersion = "2.1.0" };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual, Window);

        Assert.IsTrue(result.IsCompatible, "A caller declaring the newest protocol version still inside the supported window must be accepted (forward compatibility).");
    }

    [TestMethod]
    public void Evaluate_WithProtocolWindow_ContractProtocolVersionBelowTheWindowMinimum_IsRejected()
    {
        var actual = Expected with { ContractProtocolVersion = "1.8.9" };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual, Window);

        Assert.IsFalse(result.IsCompatible);
        Assert.AreEqual(CloudVersionComponent.ContractProtocol, result.IncompatibleComponent);
        StringAssert.Contains(result.Reason, "outside the declared supported window");
    }

    [TestMethod]
    public void Evaluate_WithProtocolWindow_ContractProtocolVersionAboveTheWindowMaximum_IsRejected()
    {
        var actual = Expected with { ContractProtocolVersion = "2.1.1" };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual, Window);

        Assert.IsFalse(result.IsCompatible);
        Assert.AreEqual(CloudVersionComponent.ContractProtocol, result.IncompatibleComponent);
    }

    [TestMethod]
    public void Evaluate_WithProtocolWindow_StillRequiresAceExtensionAndCloudSchemaToMatchExactly()
    {
        var actual = Expected with { ContractProtocolVersion = "2.0.0", AceExtensionVersion = "1.1.0" };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual, Window);

        Assert.IsFalse(result.IsCompatible, "A supported protocol window only relaxes the contract protocol check; the ACE extension version must still match exactly.");
        Assert.AreEqual(CloudVersionComponent.AceExtension, result.IncompatibleComponent);
    }

    [TestMethod]
    public void Evaluate_WithoutAProtocolWindow_StillRequiresTheContractProtocolVersionToMatchExactly_EvenIfItWouldFallInsideSomeWindow()
    {
        // Deliberate-fault contrast (issue #10 Red section): this proves the window parameter is
        // load-bearing rather than a no-op. Without it, "1.9.0" -- which Window above would happily
        // accept -- must still be rejected exactly like every other unmatched string. If a future
        // change accidentally made the window's relaxed check apply unconditionally, this test
        // would start failing.
        var actual = Expected with { ContractProtocolVersion = "1.9.0" };

        var result = CloudCompatibilityChecker.Evaluate(Expected, actual);

        Assert.IsFalse(result.IsCompatible);
        Assert.AreEqual(CloudVersionComponent.ContractProtocol, result.IncompatibleComponent);
    }
}
